using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Jobs;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Workers;

// T-039, section 9.9, US-40, US-41 and US-42: advance the clock, run the job, assert the state.
public sealed class ScheduledJobTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakePaymentProvider Provider => Factory.Services.GetRequiredService<FakePaymentProvider>();

    [Fact]
    public async Task US40_Expiry_ExpiresOnlyOverduePendingOrdersAndAuditsThem()
    {
        var (buyer, overdue) = await PendingOrderAsync("expiry");
        using (buyer)
        {
            Time.Advance(TimeSpan.FromMinutes(31));
            using var fresh = await AsBuyerAsync("expiry-fresh@example.test");
            using var seller = await AsSellerAsync("expiry-seller2@example.test");
            var recent = await OrderIdAsync(fresh, await CreatePublishedProductAsync(seller));

            (await RunAsync<OrderExpiryJob>()).ShouldBe(1);
            (await RunAsync<OrderExpiryJob>()).ShouldBe(0);

            var expired = await WithDbAsync(db => db.Orders.SingleAsync(order => order.Id == overdue, CancellationToken));
            expired.Status.ShouldBe(OrderStatus.Expired);
            expired.ExpiredAt.ShouldBe(Time.GetUtcNow());
            (await WithDbAsync(db => db.Orders.Where(order => order.Id == recent).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Pending);
            (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "order.expired" && entry.EntityId == overdue, CancellationToken))).ShouldBe(1);
        }
    }

    [Fact]
    public async Task US41_Reconciliation_MarksPaidWhenTheWebhookWasMissed()
    {
        var (orderId, providerPaymentId) = await PaymentAsync("missed");
        Provider.CompletePayment(providerPaymentId);

        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(0, "payments younger than 10 minutes are left alone");
        Time.Advance(TimeSpan.FromMinutes(11));
        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(1);

        (await WithDbAsync(db => db.Orders.Where(order => order.Id == orderId).Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Paid);
        (await WithDbAsync(db => db.Payments.Select(payment => payment.Status).SingleAsync(CancellationToken))).ShouldBe(PaymentStatus.Succeeded);

        // FM-21: the late webhook, signed now so it is inside the signature tolerance, changes nothing.
        await DeliverAsync(Provider.CompletePayment(providerPaymentId));
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(1);
        (await WithDbAsync(db => db.LedgerEntries.CountAsync(CancellationToken))).ShouldBe(2);
    }

    [Fact]
    public async Task US41_Reconciliation_MarksFailedWhenTheProviderReportsFailure()
    {
        var (_, providerPaymentId) = await PaymentAsync("failed");
        Provider.FailPayment(providerPaymentId);
        Time.Advance(TimeSpan.FromMinutes(11));

        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(1);

        var payment = await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken));
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureCode.ShouldBe("reconciled_failed");
    }

    [Fact]
    public async Task US41_Reconciliation_CancelsPaymentsStillUnpaidAfter24Hours()
    {
        var (_, providerPaymentId) = await PaymentAsync("stale");
        Time.Advance(TimeSpan.FromMinutes(11));
        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(0, "an unpaid payment under 24 hours old stays pending");

        Time.Advance(TimeSpan.FromHours(24));
        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(1);

        var payment = await WithDbAsync(db => db.Payments.SingleAsync(CancellationToken));
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureCode.ShouldBe("reconciliation_timeout");
        (await Provider.GetPaymentAsync(providerPaymentId, CancellationToken)).Status.ShouldBe(Application.Payments.ProviderPaymentStatus.Canceled);
    }

    [Fact]
    public async Task US41_Reconciliation_IgnoresPaymentsOlderThan48Hours()
    {
        var (_, providerPaymentId) = await PaymentAsync("ancient");
        Provider.CompletePayment(providerPaymentId);
        Time.Advance(TimeSpan.FromHours(49));

        (await RunAsync<PaymentReconciliationJob>()).ShouldBe(0);

        (await WithDbAsync(db => db.Payments.Select(payment => payment.Status).SingleAsync(CancellationToken))).ShouldBe(PaymentStatus.RequiresPayment);
    }

    [Fact]
    public async Task US42_Cleanup_AppliesTheRetentionTable()
    {
        // Registration and sign-in leave one-time tokens, refresh tokens and processed outbox rows; ordering leaves
        // an idempotency key.
        var (buyer, _) = await PendingOrderAsync("cleanup");
        buyer.Dispose();
        await ProcessOutboxAsync();
        var deadLetteredId = await WithDbAsync(async db =>
        {
            var message = await db.OutboxMessages.OrderBy(candidate => candidate.CreatedAt).FirstAsync(CancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE outbox_messages SET processed_at = NULL, dead_lettered_at = {Time.GetUtcNow()} WHERE id = {message.Id}", CancellationToken);
            return message.Id;
        });

        await RunAsync<CleanupJob>();
        (await WithDbAsync(db => db.IdempotencyKeys.CountAsync(CancellationToken))).ShouldBeGreaterThan(0);
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(CancellationToken))).ShouldBeGreaterThan(0);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(message => message.ProcessedAt != null, CancellationToken))).ShouldBeGreaterThan(0);

        // Past every rule: refresh tokens expire, then are kept 30 more days.
        Time.Advance(TimeSpan.FromDays(90));
        (await RunAsync<CleanupJob>()).ShouldBeGreaterThan(0);

        (await WithDbAsync(db => db.IdempotencyKeys.CountAsync(CancellationToken))).ShouldBe(0);
        (await WithDbAsync(db => db.OneTimeTokens.CountAsync(CancellationToken))).ShouldBe(0);
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(CancellationToken))).ShouldBe(0);
        (await WithDbAsync(db => db.OutboxMessages.Select(message => message.Id).ToListAsync(CancellationToken))).ShouldBe([deadLetteredId]);
        (await WithDbAsync(db => db.AuditLog.CountAsync(CancellationToken))).ShouldBeGreaterThan(0);
    }

    private async Task<int> RunAsync<TJob>()
        where TJob : IScheduledJob
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TJob>().RunAsync(CancellationToken);
    }

    private async Task ProcessOutboxAsync()
    {
        var processor = Factory.Services.GetRequiredService<Infrastructure.Outbox.OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(CancellationToken) > 0; round++)
        {
        }
    }

    private async Task<(Guid OrderId, string ProviderPaymentId)> PaymentAsync(string prefix)
    {
        var (buyer, orderId) = await PendingOrderAsync(prefix);
        using (buyer)
        {
            using var pay = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payment");
            pay.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
            (await buyer.SendAsync(pay, CancellationToken)).EnsureSuccessStatusCode();
        }

        return (orderId, await WithDbAsync(db => db.Payments.Select(payment => payment.ProviderPaymentId).SingleAsync(CancellationToken)));
    }

    private async Task DeliverAsync((IReadOnlyDictionary<string, string> Headers, byte[] Body) webhook)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new ByteArrayContent(webhook.Body) };
        foreach (var (name, value) in webhook.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        (await AsAnonymous().SendAsync(request, CancellationToken)).EnsureSuccessStatusCode();
        await ProcessOutboxAsync();
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
