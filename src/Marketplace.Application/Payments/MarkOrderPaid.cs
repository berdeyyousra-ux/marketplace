using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Fulfillment;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Payments;

public enum MarkPaidOutcome
{
    Paid,
    AlreadyPaid,
    RefundScheduled,
}

// Row locks the Application layer cannot express in LINQ (section 4.6).
public interface IRowLocks
{
    Task LockOrderAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed record PaymentMessage(Guid PaymentId);

public sealed record OrderMessage(Guid OrderId);

// Section 9.6, with the 2026-11-02 decisions: an amount mismatch is audited in its own transaction, and a payment
// for a product the buyer already owns is refunded. Used by webhook processing and reconciliation (T-039).
public sealed class OrderPaymentService(IAppDbContext db, IRowLocks locks, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider)
{
    public async Task<MarkPaidOutcome> MarkOrderPaidAsync(Guid paymentId, Money reportedAmount, AuditActorType actor, CancellationToken cancellationToken)
    {
        var orderId = await db.Payments.Where(payment => payment.Id == paymentId).Select(payment => payment.OrderId).SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        // The total never changes, so the mismatch check runs before the transaction, and its audit row is saved
        // on its own, before the message is dead-lettered (FM-08, changelog 2026-11-02).
        var total = await db.Orders.Where(order => order.Id == orderId).Select(order => order.Total).SingleAsync(cancellationToken);
        if (reportedAmount != total)
        {
            audit.Record("payment.amount_mismatch", actor, entityType: "Payment", entityId: paymentId,
                details: new { orderId, reportedAmount = reportedAmount.Amount, reportedCurrency = reportedAmount.Currency });
            await db.SaveChangesAsync(cancellationToken);
            throw new NonRetryableException($"Payment {paymentId} reported {reportedAmount} but order {orderId} totals {total}.");
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(orderId, cancellationToken);
        var order = await db.Orders.Include(candidate => candidate.Items).SingleAsync(candidate => candidate.Id == orderId, cancellationToken);
        var payment = await db.Payments.SingleAsync(candidate => candidate.Id == paymentId, cancellationToken);

        if (order.Status is OrderStatus.Paid or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            if (payment.Status != PaymentStatus.RequiresPayment)
            {
                return MarkPaidOutcome.AlreadyPaid;
            }

            // A second payment succeeded for an order that another payment already paid: refund it.
            return await ScheduleRefundAsync(transaction, payment, order.Id, actor, "payment.duplicate_charge_refund_scheduled", now, cancellationToken);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return await ScheduleRefundAsync(transaction, payment, order.Id, actor, "payment.auto_refund_scheduled", now, cancellationToken);
        }

        var productIds = order.Items.Select(item => item.ProductId).ToList();
        if (await db.DownloadGrants.AnyAsync(grant => grant.BuyerId == order.BuyerId && productIds.Contains(grant.ProductId) && grant.RevokedAt == null, cancellationToken))
        {
            // The buyer already owns a product here, through another paid order (changelog 2026-11-02).
            if (order.Status == OrderStatus.Pending)
            {
                order.Cancel(now);
            }

            return await ScheduleRefundAsync(transaction, payment, order.Id, actor, "payment.duplicate_purchase_refund_scheduled", now, cancellationToken);
        }

        Require(payment.MarkSucceeded(now));
        Require(order.MarkPaid(now));
        foreach (var item in order.Items)
        {
            db.DownloadGrants.Add(DownloadGrant.Create(item.Id, order.BuyerId, item.ProductId, now));
            db.LedgerEntries.AddRange(LedgerEntry.ForPaidItem(item, now));
        }

        audit.Record("order.paid", actor, entityType: "Order", entityId: order.Id);
        audit.Record("payment.succeeded", actor, entityType: "Payment", entityId: payment.Id);
        audit.Record("ledger.entries_created", actor, entityType: "Order", entityId: order.Id, details: new { items = order.Items.Count });
        outbox.Enqueue(OutboxMessageTypes.OrderReceiptEmail, new OrderMessage(order.Id));

        // A concurrent worker finishing first makes this throw on a unique index (section 9.6 step 7). The outbox
        // retries the message, and the retry finds the order already paid.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MarkPaidOutcome.Paid;
    }

    private async Task<MarkPaidOutcome> ScheduleRefundAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, Payment payment, Guid orderId, AuditActorType actor, string action,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.RequiresPayment)
        {
            Require(payment.MarkSucceeded(now));
        }

        outbox.Enqueue(OutboxMessageTypes.AutoRefundPayment, new PaymentMessage(payment.Id));
        audit.Record(action, actor, entityType: "Payment", entityId: payment.Id, details: new { orderId });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MarkPaidOutcome.RefundScheduled;
    }

    private static void Require(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.Message);
        }
    }
}
