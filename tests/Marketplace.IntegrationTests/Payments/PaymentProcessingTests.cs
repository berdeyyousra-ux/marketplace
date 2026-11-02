using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Payments;

// US-24, section 9.5 (process), section 9.6 and FM-04 to FM-08 and FM-21.
public sealed class PaymentProcessingTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakePaymentProvider Provider => Factory.Services.GetRequiredService<FakePaymentProvider>();

    [Fact]
    public async Task US24_SucceededWebhook_MarksPaidWithGrantsLedgerAndReceipt()
    {
        var flow = await PayAsync("paid");

        await DeliverAsync(Provider.CompletePayment(flow.ProviderPaymentId));
        await ProcessAllAsync();

        var order = await WithDbAsync(db => db.Orders.SingleAsync(candidate => candidate.Id == flow.OrderId, CancellationToken));
        order.Status.ShouldBe(OrderStatus.Paid);
        order.PaidAt.ShouldBe(Time.GetUtcNow());
        (await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken))).Status.ShouldBe(PaymentStatus.Succeeded);
        var grant = await WithDbAsync(db => db.DownloadGrants.SingleAsync(CancellationToken));
        grant.ExpiresAt.ShouldBe(Time.GetUtcNow().AddDays(365));
        var ledger = await WithDbAsync(db => db.LedgerEntries.Select(entry => new { entry.EntryType, entry.Amount }).ToListAsync(CancellationToken));
        ledger.Select(entry => (entry.EntryType, entry.Amount.Amount)).Order().ShouldBe(new[] { (LedgerEntryType.Sale, 19.99m), (LedgerEntryType.PlatformFee, -2.00m) }.Order());
        (await OutboxTypesAsync()).ShouldContain(OutboxMessageTypes.OrderReceiptEmail);
        (await AuditActionsAsync()).ShouldContain("order.paid");
        (await WithDbAsync(db => db.PaymentEvents.AllAsync(stored => stored.ProcessedAt != null, CancellationToken))).ShouldBeTrue();
    }

    [Fact]
    public async Task FM04_DuplicateEventsWithNewIds_PayOnce()
    {
        var flow = await PayAsync("duplicate");
        var webhook = Provider.CompletePayment(flow.ProviderPaymentId);
        var again = Provider.CreateWebhook(PaymentEventType.Succeeded, flow.ProviderPaymentId, Money.Eur(19.99m));

        await DeliverAsync(webhook);
        await DeliverAsync(webhook);
        await DeliverAsync(again);
        await ProcessAllAsync();

        (await WithDbAsync(db => db.PaymentEvents.CountAsync(CancellationToken))).ShouldBe(2);
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(1);
        (await WithDbAsync(db => db.LedgerEntries.CountAsync(CancellationToken))).ShouldBe(2);
    }

    [Fact]
    public async Task FM05_EventBeforePaymentRow_IsRetriedAndThenSucceeds()
    {
        var (_, orderId) = await OrderAsync("early");
        var providerPayment = await Provider.CreatePaymentAsync(new CreateProviderPayment(orderId, Money.Eur(19.99m), $"order-{orderId}-attempt-1"), CancellationToken);

        await DeliverAsync(Provider.CompletePayment(providerPayment.ProviderPaymentId));
        await ProcessOnceAsync();
        var afterFirstTry = await WithDbAsync(db => db.Orders.Where(order => order.Id == orderId).Select(order => order.Status).SingleAsync(CancellationToken));
        await WithDbAsync(db =>
        {
            db.Payments.Add(Payment.Create(orderId, "Fake", providerPayment.ProviderPaymentId, Money.Eur(19.99m), Time.GetUtcNow()).Value);
            return db.SaveChangesAsync(CancellationToken);
        });
        Time.Advance(TimeSpan.FromMinutes(1));
        await ProcessAllAsync();

        afterFirstTry.ShouldBe(OrderStatus.Pending);
        (await WithDbAsync(db => db.Orders.Where(order => order.Id == orderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task FM06_SucceededForExpiredOrder_BecomesPaid()
    {
        var flow = await PayAsync("expired");
        Time.Advance(TimeSpan.FromMinutes(31));
        await WithDbAsync(async db =>
        {
            var order = await db.Orders.SingleAsync(candidate => candidate.Id == flow.OrderId, CancellationToken);
            order.Expire(Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            return await db.SaveChangesAsync(CancellationToken);
        });

        await DeliverAsync(Provider.CompletePayment(flow.ProviderPaymentId));
        await ProcessAllAsync();

        (await WithDbAsync(db => db.Orders.Where(order => order.Id == flow.OrderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Paid);
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task FM07_SucceededForCancelledOrder_StaysCancelledAndIsRefunded()
    {
        var flow = await PayAsync("cancelled");
        await WithDbAsync(async db =>
        {
            var order = await db.Orders.SingleAsync(candidate => candidate.Id == flow.OrderId, CancellationToken);
            order.Cancel(Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            return await db.SaveChangesAsync(CancellationToken);
        });

        await DeliverAsync(Provider.CompletePayment(flow.ProviderPaymentId));
        await ProcessAllAsync();

        (await WithDbAsync(db => db.Orders.Where(order => order.Id == flow.OrderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Cancelled);
        (await AuditActionsAsync()).ShouldContain("payment.auto_refund_scheduled");
        (await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken))).Status.ShouldBe(PaymentStatus.Refunded);
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task FM08_AmountMismatch_DeadLettersAndAuditsWhileOrderStaysPending()
    {
        var flow = await PayAsync("mismatch");

        await DeliverAsync(Provider.CompletePayment(flow.ProviderPaymentId, reportedAmount: Money.Eur(1.00m)));
        await ProcessAllAsync();

        (await WithDbAsync(db => db.Orders.Where(order => order.Id == flow.OrderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Pending);
        (await AuditActionsAsync()).ShouldContain("payment.amount_mismatch");
        var message = await WithDbAsync(db => db.OutboxMessages.SingleAsync(candidate => candidate.Type == OutboxMessageTypes.ProcessPaymentEvent, CancellationToken));
        message.DeadLetteredAt.ShouldNotBeNull();
        message.Attempts.ShouldBe(1);
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task FM21_ConcurrentMarkPaid_CreatesOneSetOfGrantsAndLedgerRows()
    {
        var flow = await PayAsync("concurrent");
        Provider.CompletePayment(flow.ProviderPaymentId);

        var outcomes = await Task.WhenAll(MarkPaidInNewScopeAsync(flow.PaymentId), MarkPaidInNewScopeAsync(flow.PaymentId));

        outcomes.Order().ShouldBe(new[] { MarkPaidOutcome.Paid, MarkPaidOutcome.AlreadyPaid }.Order());
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(1);
        (await WithDbAsync(db => db.LedgerEntries.CountAsync(CancellationToken))).ShouldBe(2);
    }

    [Fact]
    public async Task SecondPaidOrderForSameProduct_IsCancelledAndRefunded()
    {
        using var seller = await AsSellerAsync("double-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var buyer = await AsBuyerAsync("double-buyer@example.test");
        var first = await PayExistingAsync(buyer, await OrderIdAsync(buyer, productId));
        var second = await PayExistingAsync(buyer, await OrderIdAsync(buyer, productId));

        await DeliverAsync(Provider.CompletePayment(first.ProviderPaymentId));
        await DeliverAsync(Provider.CompletePayment(second.ProviderPaymentId));
        await ProcessAllAsync();

        // Processed in parallel, one order can lose the race on the active-grant unique index (section 9.6 step 7).
        // Its message is retried after the backoff, and the retry takes the duplicate-purchase path.
        Time.Advance(TimeSpan.FromMinutes(1));
        await ProcessAllAsync();

        var statuses = await WithDbAsync(db => db.Orders.OrderBy(order => order.CreatedAt).ThenBy(order => order.Id).Select(order => order.Status).ToListAsync(CancellationToken));
        statuses.Order().ShouldBe(new[] { OrderStatus.Paid, OrderStatus.Cancelled }.Order());
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(1);
        (await WithDbAsync(db => db.Payments.CountAsync(payment => payment.Status == PaymentStatus.Refunded, CancellationToken))).ShouldBe(1);
        (await AuditActionsAsync()).ShouldContain("payment.duplicate_purchase_refund_scheduled");
    }

    [Fact]
    public async Task FailedWebhook_MarksPaymentFailedAndLeavesOrderPending()
    {
        var flow = await PayAsync("failed");

        await DeliverAsync(Provider.FailPayment(flow.ProviderPaymentId));
        await ProcessAllAsync();

        (await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken))).Status.ShouldBe(PaymentStatus.Failed);
        (await WithDbAsync(db => db.Orders.Where(order => order.Id == flow.OrderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task DisputedWebhook_IsAuditedWithoutAutomatedAction()
    {
        var flow = await PayAsync("disputed");

        await DeliverAsync(Provider.CreateWebhook(PaymentEventType.Disputed, flow.ProviderPaymentId, Money.Eur(19.99m)));
        await ProcessAllAsync();

        (await AuditActionsAsync()).ShouldContain("payment.disputed");
        (await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken))).Status.ShouldBe(PaymentStatus.RequiresPayment);
    }

    private sealed record Flow(Guid OrderId, Guid PaymentId, string ProviderPaymentId);

    private async Task<(HttpClient Buyer, Guid OrderId)> OrderAsync(string prefix)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        return (buyer, await OrderIdAsync(buyer, productId));
    }

    private async Task<Flow> PayAsync(string prefix)
    {
        var (buyer, orderId) = await OrderAsync(prefix);
        using (buyer)
        {
            return await PayExistingAsync(buyer, orderId);
        }
    }

    private static async Task<Guid> OrderIdAsync(HttpClient buyer, Guid productId) =>
        (await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

    private async Task<Flow> PayExistingAsync(HttpClient buyer, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payment");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await buyer.SendAsync(request, CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paymentId = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("paymentId").GetGuid();
        var providerPaymentId = await WithDbAsync(db => db.Payments.Where(payment => payment.Id == paymentId).Select(payment => payment.ProviderPaymentId).SingleAsync(CancellationToken));
        return new Flow(orderId, paymentId, providerPaymentId);
    }

    private async Task DeliverAsync((IReadOnlyDictionary<string, string> Headers, byte[] Body) webhook)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new ByteArrayContent(webhook.Body) };
        foreach (var (name, value) in webhook.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        (await AsAnonymous().SendAsync(request, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<int> ProcessOnceAsync() => Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

    // Processes until nothing is due, including messages that handlers queue along the way.
    private async Task ProcessAllAsync()
    {
        for (var round = 0; round < 10 && await ProcessOnceAsync() > 0; round++)
        {
        }
    }

    private async Task<MarkPaidOutcome> MarkPaidInNewScopeAsync(Guid paymentId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderPaymentService>()
            .MarkOrderPaidAsync(paymentId, Money.Eur(19.99m), AuditActorType.Worker, CancellationToken);
    }

    private Task<List<string>> OutboxTypesAsync() => WithDbAsync(db => db.OutboxMessages.Select(message => message.Type).ToListAsync(CancellationToken));

    private Task<List<string>> AuditActionsAsync() => WithDbAsync(db => db.AuditLog.Select(entry => entry.Action).ToListAsync(CancellationToken));
}
