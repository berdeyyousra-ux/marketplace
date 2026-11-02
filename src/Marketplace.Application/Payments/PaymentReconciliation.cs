using Marketplace.Application.Abstractions;
using Marketplace.Application.Ops;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marketplace.Application.Payments;

public enum ReconciliationOutcome
{
    StillPending,
    Paid,
    Failed,
    TimedOut,
    Skipped,
}

// Section 9.9 reconciliation and US-41, with the 2026-11-02 window: RequiresPayment payments created 10 minutes to
// 48 hours ago are checked with the provider. Those still unpaid after 24 hours are cancelled and marked Failed.
public sealed partial class PaymentReconciler(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<PaymentReconciler> logger)
{
    public const int BatchSize = 100;
    public static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan GiveUpAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(48);

    // Returns how many payments changed state.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var newest = now - MinimumAge;
        var oldest = now - MaximumAge;
        List<Guid> paymentIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            paymentIds = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Payments.AsNoTracking()
                .Where(payment => payment.Status == PaymentStatus.RequiresPayment && payment.CreatedAt < newest && payment.CreatedAt > oldest)
                .OrderBy(payment => payment.CreatedAt)
                .Take(BatchSize)
                .Select(payment => payment.Id)
                .ToListAsync(cancellationToken);
        }

        var changed = 0;
        foreach (var paymentId in paymentIds)
        {
            // One scope per payment, so a failure leaves no tracked state behind for the next one.
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var outcome = await ReconcileAsync(scope.ServiceProvider, paymentId, cancellationToken);
                if (outcome is not (ReconciliationOutcome.StillPending or ReconciliationOutcome.Skipped))
                {
                    changed++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReconcileFailed(logger, paymentId, exception);
            }
        }

        return changed;
    }

    private async Task<ReconciliationOutcome> ReconcileAsync(IServiceProvider services, Guid paymentId, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var provider = services.GetRequiredService<IPaymentProvider>();
        // Not tracked: MarkFailedAsync reloads it under the order lock.
        var payment = await db.Payments.AsNoTracking().SingleAsync(candidate => candidate.Id == paymentId, cancellationToken);
        if (payment.Status != PaymentStatus.RequiresPayment)
        {
            return ReconciliationOutcome.Skipped;
        }

        var current = await provider.GetPaymentAsync(payment.ProviderPaymentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        switch (current.Status)
        {
            case ProviderPaymentStatus.Succeeded:
                // The webhook was missed or is late; the same path is safe if it arrives afterwards (FM-21).
                await services.GetRequiredService<OrderPaymentService>()
                    .MarkOrderPaidAsync(payment.Id, current.Amount, AuditActorType.Worker, cancellationToken);
                return ReconciliationOutcome.Paid;

            case ProviderPaymentStatus.Failed or ProviderPaymentStatus.Canceled:
                return await MarkFailedAsync(services, db, payment, "reconciled_" + current.Status.ToString().ToLowerInvariant(), now, cancellationToken)
                    ? ReconciliationOutcome.Failed
                    : ReconciliationOutcome.Skipped;

            case ProviderPaymentStatus.RequiresPayment when now - payment.CreatedAt > GiveUpAge:
                await provider.CancelPaymentAsync(payment.ProviderPaymentId, cancellationToken);
                var afterCancel = await provider.GetPaymentAsync(payment.ProviderPaymentId, cancellationToken);
                if (afterCancel.Status == ProviderPaymentStatus.Succeeded)
                {
                    // Paid at the last moment: the order is paid, or refunded if it can no longer be fulfilled.
                    await services.GetRequiredService<OrderPaymentService>()
                        .MarkOrderPaidAsync(payment.Id, afterCancel.Amount, AuditActorType.Worker, cancellationToken);
                    return ReconciliationOutcome.Paid;
                }

                return await MarkFailedAsync(services, db, payment, "reconciliation_timeout", now, cancellationToken)
                    ? ReconciliationOutcome.TimedOut
                    : ReconciliationOutcome.Skipped;

            default:
                return ReconciliationOutcome.StillPending;
        }
    }

    // Takes the order row lock that MarkOrderPaid takes, so a webhook processed at the same moment cannot be
    // overwritten: whichever runs second sees the other's result.
    private static async Task<bool> MarkFailedAsync(
        IServiceProvider services, IAppDbContext db, Payment unlocked, string failureCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await services.GetRequiredService<IRowLocks>().LockOrderAsync(unlocked.OrderId, cancellationToken);
        var payment = await db.Payments.SingleAsync(candidate => candidate.Id == unlocked.Id, cancellationToken);
        if (payment.MarkFailed(failureCode, now).IsFailure)
        {
            return false;
        }

        services.GetRequiredService<AuditTrail>().Record(
            "payment.reconciled_failed", AuditActorType.Worker, entityType: "Payment", entityId: payment.Id, details: new { failureCode });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciling payment {PaymentId} failed; it is retried on the next run")]
    private static partial void LogReconcileFailed(ILogger logger, Guid paymentId, Exception exception);
}
