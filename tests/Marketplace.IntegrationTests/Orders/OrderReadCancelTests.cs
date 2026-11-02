using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Application.Payments;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Orders;

// EP-31, EP-32, EP-34, US-25 and US-27.
public sealed class OrderReadCancelTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US25_ListOrders_ReturnsOnlyOwnOrdersNewestFirstWithPaging()
    {
        using var seller = await AsSellerAsync("list-seller@example.test");
        var productIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            productIds.Add(await CreatePublishedProductAsync(seller, $"Product {i}"));
        }

        using var buyer = await AsBuyerAsync("list-buyer@example.test");
        using var other = await AsBuyerAsync("list-other@example.test");
        var mine = new List<Guid>();
        foreach (var productId in productIds)
        {
            mine.Add(await OrderIdAsync(buyer, productId));
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        await OrderIdAsync(other, productIds[0]);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/orders?limit=2{(cursor is null ? string.Empty : "&cursor=" + cursor)}", CancellationToken);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        seen.ShouldBe(Enumerable.Reverse(mine).ToList());
    }

    [Fact]
    public async Task US25_StatusFilter_ReturnsMatchingOrders()
    {
        var (buyer, orderId) = await PendingOrderAsync("filter");
        using (buyer)
        {
            await buyer.PostAsync($"/api/v1/orders/{orderId}/cancel", content: null, CancellationToken);

            var cancelled = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=Cancelled", CancellationToken);
            var pending = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=Pending", CancellationToken);

            cancelled.GetProperty("items").GetArrayLength().ShouldBe(1);
            pending.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task US25_GetOrder_ShowsItemsAndLatestPaymentToBuyerAndAdmin()
    {
        var (buyer, orderId) = await PendingOrderAsync("detail");
        using (buyer)
        {
            using var pay = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payment");
            pay.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
            (await buyer.SendAsync(pay, CancellationToken)).EnsureSuccessStatusCode();
            using var stranger = await AsBuyerAsync("detail-stranger@example.test");
            using var admin = await AsAdminAsync();

            var detail = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}", CancellationToken);
            var forStranger = await stranger.GetAsync($"/api/v1/orders/{orderId}", CancellationToken);
            var forAdmin = await admin.GetAsync($"/api/v1/orders/{orderId}", CancellationToken);

            detail.GetProperty("items").GetArrayLength().ShouldBe(1);
            detail.GetProperty("payment").GetProperty("status").GetString().ShouldBe("RequiresPayment");
            detail.GetProperty("payment").GetProperty("amount").GetProperty("amount").GetString().ShouldBe("19.99");
            forStranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            forAdmin.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task US27_CancelPending_CancelsOrderAndLivePayment()
    {
        var (buyer, orderId) = await PendingOrderAsync("cancel");
        using (buyer)
        {
            using var pay = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payment");
            pay.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
            (await buyer.SendAsync(pay, CancellationToken)).EnsureSuccessStatusCode();

            var response = await buyer.PostAsync($"/api/v1/orders/{orderId}/cancel", content: null, CancellationToken);
            await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await WithDbAsync(db => db.Orders.Where(order => order.Id == orderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Cancelled);
            (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "order.cancelled", CancellationToken))).ShouldBe(1);
            (await WithDbAsync(db => db.OutboxMessages.CountAsync(message => message.Type == OutboxMessageTypes.CancelPaymentIntent, CancellationToken))).ShouldBe(1);
            var payment = await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken));
            payment.Status.ShouldBe(PaymentStatus.Failed);
            var atProvider = await Factory.Services.GetRequiredService<FakePaymentProvider>().GetPaymentAsync(payment.ProviderPaymentId, CancellationToken);
            atProvider.Status.ShouldBe(ProviderPaymentStatus.Canceled);
        }
    }

    [Fact]
    public async Task US27_CancelPaidOrTwice_Returns409()
    {
        var (buyer, orderId) = await PendingOrderAsync("cancel-paid");
        var (secondBuyer, secondOrderId) = await PendingOrderAsync("cancel-twice");
        using (buyer)
        using (secondBuyer)
        {
            await WithDbAsync(async db =>
            {
                var order = await db.Orders.SingleAsync(candidate => candidate.Id == orderId, CancellationToken);
                order.MarkPaid(Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
                return await db.SaveChangesAsync(CancellationToken);
            });
            await secondBuyer.PostAsync($"/api/v1/orders/{secondOrderId}/cancel", content: null, CancellationToken);

            var paid = await buyer.PostAsync($"/api/v1/orders/{orderId}/cancel", content: null, CancellationToken);
            var twice = await secondBuyer.PostAsync($"/api/v1/orders/{secondOrderId}/cancel", content: null, CancellationToken);

            paid.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await paid.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("invalid_state_transition");
            twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    private async Task<(HttpClient Buyer, Guid OrderId)> PendingOrderAsync(string prefix)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        return (buyer, await OrderIdAsync(buyer, productId));
    }

    private static async Task<Guid> OrderIdAsync(HttpClient buyer, Guid productId) =>
        (await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
}
