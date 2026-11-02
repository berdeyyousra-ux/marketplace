using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Orders;

// EP-33, US-23 and section 9.4.
public sealed class CreatePaymentTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakePaymentProvider Provider => Factory.Services.GetRequiredService<FakePaymentProvider>();

    [Fact]
    public async Task US23_Pay_ReturnsClientSecretAndRecordsPayment()
    {
        var (buyer, orderId) = await PendingOrderAsync("pay");

        var response = await PayAsync(buyer, orderId);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("provider").GetString().ShouldBe("Fake");
        body.GetProperty("clientSecret").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("amount").GetProperty("amount").GetString().ShouldBe("19.99");
        var payment = await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken));
        payment.Id.ShouldBe(body.GetProperty("paymentId").GetGuid());
        payment.Status.ShouldBe(PaymentStatus.RequiresPayment);
        payment.Amount.Amount.ShouldBe(19.99m);
        (await Provider.GetPaymentAsync(payment.ProviderPaymentId, CancellationToken)).Amount.Amount.ShouldBe(19.99m);
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "payment.created", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US23_PayAgainWithNewKey_ReusesLivePayment()
    {
        var (buyer, orderId) = await PendingOrderAsync("pay-again");

        var first = await (await PayAsync(buyer, orderId)).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var second = await (await PayAsync(buyer, orderId)).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        second.GetProperty("paymentId").GetGuid().ShouldBe(first.GetProperty("paymentId").GetGuid());
        second.GetProperty("clientSecret").GetString().ShouldBe(first.GetProperty("clientSecret").GetString());
        (await WithDbAsync(db => db.Payments.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US23_ConcurrentPayments_EndWithOnePayment()
    {
        var (buyer, orderId) = await PendingOrderAsync("pay-race");

        var responses = await Task.WhenAll(PayAsync(buyer, orderId), PayAsync(buyer, orderId));
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            ids.Add((await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("paymentId").GetGuid());
        }

        ids.Distinct().Count().ShouldBe(1);
        (await WithDbAsync(db => db.Payments.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US23_ExpiredOrder_Returns409PaymentNotAllowed()
    {
        var (buyer, orderId) = await PendingOrderAsync("pay-expired");

        Time.Advance(TimeSpan.FromMinutes(31));
        using var signedInAgain = WithToken((await SignInAsync("pay-expired-buyer@example.test")).AccessToken);
        var response = await PayAsync(signedInAgain, orderId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("payment_not_allowed");
    }

    [Fact]
    public async Task US23_SomeoneElsesOrder_Returns404()
    {
        var (_, orderId) = await PendingOrderAsync("pay-owner");
        using var stranger = await AsBuyerAsync("pay-stranger@example.test");

        (await PayAsync(stranger, orderId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task US23_ProviderDown_Returns503AndRecordsNothing()
    {
        var (buyer, orderId) = await PendingOrderAsync("pay-down");
        Provider.FailNext(1);

        var response = await PayAsync(buyer, orderId);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("payment_provider_unavailable");
        (await WithDbAsync(db => db.Payments.CountAsync(CancellationToken))).ShouldBe(0);
    }

    private async Task<(HttpClient Buyer, Guid OrderId)> PendingOrderAsync(string prefix)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return (buyer, order.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient buyer, Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payment");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return buyer.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
