using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Payments;

// EP-40 and section 9.5 (receive).
public sealed record ReceiveWebhookCommand(IReadOnlyDictionary<string, string> Headers, byte[] RawBody);

public sealed record PaymentEventMessage(Guid PaymentEventId);

// The webhook inbox (D-22): INSERT ... ON CONFLICT (provider, provider_event_id) DO NOTHING. Returns the new row's
// ID, or null when the event was already stored. Runs inside the caller's transaction.
public interface IPaymentEventInbox
{
    Task<Guid?> TryStoreAsync(string provider, PaymentWebhookResult webhook, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}

public static class WebhookErrors
{
    public static readonly Error SignatureInvalid =
        new("webhook_signature_invalid", "The webhook signature could not be verified.", ErrorType.Validation);
}

// Verify and store only; processing happens in the Worker (section 4.2). New and duplicate events both succeed,
// so the provider stops retrying. If the database is down the request fails with 503 and the provider retries.
public sealed class ReceiveWebhookCommandHandler(
    IPaymentProvider provider, IPaymentEventInbox inbox, IAppDbContext db, IOutbox outbox, TimeProvider timeProvider)
    : ICommandHandler<ReceiveWebhookCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReceiveWebhookCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PaymentWebhookResult webhook;
        try
        {
            webhook = provider.ParseWebhook(command.Headers, command.RawBody);
        }
        catch (WebhookSignatureException)
        {
            return WebhookErrors.SignatureInvalid;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var eventId = await inbox.TryStoreAsync(provider.Name, webhook, timeProvider.GetUtcNow(), cancellationToken);
        if (eventId is not null)
        {
            outbox.Enqueue(OutboxMessageTypes.ProcessPaymentEvent, new PaymentEventMessage(eventId.Value));
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
