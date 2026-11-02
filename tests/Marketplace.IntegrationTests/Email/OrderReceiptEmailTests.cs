using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Orders;
using Marketplace.Application.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Email;

// T-038 and section 9.10: OrderReceiptEmail is processed by the outbox and captured by FakeEmailSender.
public sealed class OrderReceiptEmailTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private FakeEmailSender Emails => Factory.Services.GetRequiredService<FakeEmailSender>();

    [Fact]
    public async Task PaidOrder_SendsOneReceiptWithTheOrderSummary()
    {
        var (buyer, orderId) = await PendingOrderAsync("receipt");
        using (buyer)
        {
            await PayOrderAsync(buyer, orderId);

            var receipt = Emails.To("receipt-buyer@example.test").Where(message => message.Subject.StartsWith("Your receipt", StringComparison.Ordinal)).ShouldHaveSingleItem();
            receipt.Subject.ShouldBe($"Your receipt for order {orderId.ToString("N")[..8].ToUpperInvariant()}");
            receipt.TextBody.ShouldContain(orderId.ToString());
            receipt.TextBody.ShouldContain("- Notion Budget Template: 19.99 EUR");
            receipt.TextBody.ShouldContain("Total: 19.99 EUR");
            receipt.TextBody.ShouldContain("/library");
        }
    }

    [Fact]
    public async Task UnpaidOrder_SendsNothing()
    {
        var (buyer, orderId) = await PendingOrderAsync("unpaid");
        using (buyer)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<Marketplace.Application.Abstractions.IOutboxHandler>().OfType<OrderReceiptEmailHandler>().Single();

            await handler.HandleAsync(JsonSerializer.Serialize(new OrderMessage(orderId), _json), CancellationToken);
            await handler.HandleAsync(JsonSerializer.Serialize(new OrderMessage(Guid.NewGuid()), _json), CancellationToken);

            Emails.To("unpaid-buyer@example.test").ShouldNotContain(message => message.Subject.StartsWith("Your receipt", StringComparison.Ordinal));
        }
    }

    private async Task<(HttpClient Buyer, Guid OrderId)> PendingOrderAsync(string prefix)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return (buyer, order.GetProperty("id").GetGuid());
    }
}
