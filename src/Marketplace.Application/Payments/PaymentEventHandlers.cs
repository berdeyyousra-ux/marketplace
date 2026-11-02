using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Application.Payments;

// Section 9.5 (process). Every branch checks current state, so duplicate and out-of-order events are safe.
public sealed partial class PaymentEventProcessor(
    IAppDbContext db, IPaymentProvider provider, OrderPaymentService orders, AuditTrail audit, TimeProvider timeProvider,
    ILogger<PaymentEventProcessor> logger) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.ProcessPaymentEvent;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var eventId = (JsonSerializer.Deserialize<PaymentEventMessage>(payload, _json) ?? throw new JsonException("Missing paymentEventId.")).PaymentEventId;
        var stored = await db.PaymentEvents.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (stored is null || stored.ProcessedAt is not null)
        {
            return;
        }

        var webhook = provider.ReadStoredEvent(stored.Payload);
        switch (webhook.Type)
        {
            case PaymentEventType.Succeeded:
                await HandleSucceededAsync(stored.Provider, webhook, cancellationToken);
                break;
            case PaymentEventType.Failed:
                await HandleFailedAsync(stored.Provider, webhook, cancellationToken);
                break;
            case PaymentEventType.Refunded:
                await HandleRefundedAsync(stored.Provider, webhook, cancellationToken);
                break;
            case PaymentEventType.Disputed:
                LogDisputed(logger, webhook.ProviderPaymentId);
                audit.Record("payment.disputed", AuditActorType.Webhook, details: new { webhook.ProviderPaymentId });
                await db.SaveChangesAsync(cancellationToken);
                break;
            case PaymentEventType.Other:
            default:
                break;
        }

        var now = timeProvider.GetUtcNow();
        await db.PaymentEvents.Where(candidate => candidate.Id == eventId)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.ProcessedAt, now), cancellationToken);
    }

    private async Task HandleSucceededAsync(string providerName, PaymentWebhookResult webhook, CancellationToken cancellationToken)
    {
        // The event can arrive before the payment row commits (FM-05): throwing makes the outbox retry later.
        var paymentId = await FindPaymentIdAsync(providerName, webhook.ProviderPaymentId, cancellationToken)
            ?? throw new InvalidOperationException($"No payment row yet for {providerName} payment {webhook.ProviderPaymentId}.");
        var amount = webhook.Amount ?? throw new NonRetryableException("A succeeded event without an amount cannot be checked.");
        await orders.MarkOrderPaidAsync(paymentId, amount, AuditActorType.Webhook, cancellationToken);
    }

    private async Task HandleFailedAsync(string providerName, PaymentWebhookResult webhook, CancellationToken cancellationToken)
    {
        var paymentId = await FindPaymentIdAsync(providerName, webhook.ProviderPaymentId, cancellationToken)
            ?? throw new InvalidOperationException($"No payment row yet for {providerName} payment {webhook.ProviderPaymentId}.");
        var payment = await db.Payments.SingleAsync(candidate => candidate.Id == paymentId, cancellationToken);
        if (payment.MarkFailed("provider_reported_failure", timeProvider.GetUtcNow()).IsSuccess)
        {
            audit.Record("payment.failed", AuditActorType.Webhook, entityType: "Payment", entityId: payment.Id);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task HandleRefundedAsync(string providerName, PaymentWebhookResult webhook, CancellationToken cancellationToken)
    {
        var paymentId = await FindPaymentIdAsync(providerName, webhook.ProviderPaymentId, cancellationToken);
        if (paymentId is not null)
        {
            var orderId = await db.Payments.Where(payment => payment.Id == paymentId).Select(payment => payment.OrderId).SingleAsync(cancellationToken);
            var ours = await db.RefundRequests.AnyAsync(
                request => (request.Status == RefundRequestStatus.Executing || request.Status == RefundRequestStatus.Completed)
                    && db.OrderItems.Any(item => item.Id == request.OrderItemId && item.OrderId == orderId),
                cancellationToken);
            var autoRefund = await db.Payments.AnyAsync(payment => payment.Id == paymentId && payment.Status == PaymentStatus.Refunded, cancellationToken);
            if (ours || autoRefund)
            {
                return;
            }
        }

        audit.Record("payment.refunded_externally", AuditActorType.Webhook, entityType: "Payment", entityId: paymentId, details: new { webhook.ProviderPaymentId });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<Guid?> FindPaymentIdAsync(string providerName, string? providerPaymentId, CancellationToken cancellationToken) =>
        db.Payments.Where(payment => payment.Provider == providerName && payment.ProviderPaymentId == providerPaymentId)
            .Select(payment => (Guid?)payment.Id)
            .SingleOrDefaultAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Error, Message = "Payment {ProviderPaymentId} was disputed; no automated action in v1")]
    private static partial void LogDisputed(ILogger logger, string? providerPaymentId);
}

// Section 9.10: refunds a payment whose order cannot be fulfilled, then marks it Refunded.
public sealed class AutoRefundPaymentHandler(IAppDbContext db, IPaymentProvider provider, AuditTrail audit, TimeProvider timeProvider) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.AutoRefundPayment;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var paymentId = (JsonSerializer.Deserialize<PaymentMessage>(payload, _json) ?? throw new JsonException("Missing paymentId.")).PaymentId;
        var payment = await db.Payments.SingleAsync(candidate => candidate.Id == paymentId, cancellationToken);
        if (payment.Status == PaymentStatus.Refunded)
        {
            return;
        }

        // Outside any transaction; the idempotency key makes a retried refund safe (section 10.2).
        await provider.RefundAsync(payment.ProviderPaymentId, payment.Amount, $"auto-refund-{payment.Id}", cancellationToken);
        if (payment.MarkRefunded(timeProvider.GetUtcNow()).IsSuccess)
        {
            audit.Record("payment.auto_refunded", AuditActorType.Worker, entityType: "Payment", entityId: payment.Id);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
