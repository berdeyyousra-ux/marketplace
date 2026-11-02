using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

// Webhook inbox row (D-22). The unique provider event ID makes duplicate deliveries harmless.
public sealed class PaymentEvent : Entity
{
    private PaymentEvent(string provider, string providerEventId, string eventType, string payload, DateTimeOffset receivedAt)
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        Payload = payload;
        ReceivedAt = receivedAt;
    }

    public string Provider { get; private set; }

    public string ProviderEventId { get; private set; }

    public string EventType { get; private set; }

    public string Payload { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static PaymentEvent Receive(string provider, string providerEventId, string eventType, string payload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new PaymentEvent(provider, providerEventId, eventType, payload, now);
    }
}
