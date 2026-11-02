using Marketplace.Application.Ops;
using Marketplace.Application.Payments;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Jobs;

// A unit of scheduled Worker work. Each run gets its own DI scope.
public interface IScheduledJob
{
    string Name { get; }

    TimeSpan Interval { get; }

    // Returns how many rows the run changed, for logging.
    Task<int> RunAsync(CancellationToken cancellationToken);
}

// Section 9.9 reconciliation, every 5 minutes. The work is in the Application layer.
public sealed class PaymentReconciliationJob(PaymentReconciler reconciler) : IScheduledJob
{
    public string Name => "payment-reconciliation";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public Task<int> RunAsync(CancellationToken cancellationToken) => reconciler.RunAsync(cancellationToken);
}

internal sealed record ExpiredOrderRow(Guid Id);

// Section 9.9 expiry and US-40. SKIP LOCKED lets a second Worker replica take other rows instead of waiting.
// The time comes from TimeProvider (R-07, changelog 2026-11-02), not the database clock.
public sealed class OrderExpiryJob(AppDbContext db, AuditTrail audit, TimeProvider timeProvider) : IScheduledJob
{
    public const int BatchSize = 200;

    public string Name => "order-expiry";

    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expired = await db.Database.SqlQuery<ExpiredOrderRow>($"""
            UPDATE orders SET status = 'Expired', expired_at = {now}, updated_at = {now}
            WHERE id IN (
              SELECT id FROM orders WHERE status = 'Pending' AND expires_at < {now}
              ORDER BY expires_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED)
            RETURNING id
            """).ToListAsync(cancellationToken);

        foreach (var order in expired)
        {
            audit.Record("order.expired", AuditActorType.Worker, entityType: "Order", entityId: order.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expired.Count;
    }
}

// Section 7.7 retention, which defines US-42. Dead-lettered outbox rows, payment events, the audit log and the
// ledger are kept.
public sealed class CleanupJob(AppDbContext db, TimeProvider timeProvider) : IScheduledJob
{
    public static readonly TimeSpan OneTimeTokenRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan RefreshTokenRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan OutboxRetention = TimeSpan.FromDays(14);
    public static readonly TimeSpan RawOutputRetention = TimeSpan.FromDays(90);

    public string Name => "cleanup";

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenCutoff = now - OneTimeTokenRetention;
        var refreshCutoff = now - RefreshTokenRetention;
        var outboxCutoff = now - OutboxRetention;
        var rawOutputCutoff = now - RawOutputRetention;

        var changed = await db.Database.ExecuteSqlAsync($"DELETE FROM idempotency_keys WHERE expires_at < {now}", cancellationToken);
        changed += await db.Database.ExecuteSqlAsync(
            $"DELETE FROM one_time_tokens WHERE expires_at < {tokenCutoff} OR used_at < {tokenCutoff}", cancellationToken);
        changed += await db.Database.ExecuteSqlAsync(
            $"DELETE FROM refresh_tokens WHERE expires_at < {refreshCutoff} OR revoked_at < {refreshCutoff}", cancellationToken);
        changed += await db.Database.ExecuteSqlAsync(
            $"DELETE FROM outbox_messages WHERE processed_at < {outboxCutoff}", cancellationToken);
        changed += await db.Database.ExecuteSqlAsync(
            $"UPDATE ai_suggestions SET raw_output = NULL WHERE raw_output IS NOT NULL AND created_at < {rawOutputCutoff}", cancellationToken);
        return changed;
    }
}
