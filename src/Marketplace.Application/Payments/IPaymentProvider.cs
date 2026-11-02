using Marketplace.Domain.Common;

namespace Marketplace.Application.Payments;

// Section 9.11 and D-25.
public interface IPaymentProvider
{
    string Name { get; }

    Task<ProviderPayment> CreatePaymentAsync(CreateProviderPayment request, CancellationToken cancellationToken);

    Task<ProviderPayment> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken);

    Task CancelPaymentAsync(string providerPaymentId, CancellationToken cancellationToken);

    Task<ProviderRefund> RefundAsync(string providerPaymentId, Money amount, string idempotencyKey, CancellationToken cancellationToken);

    // Throws WebhookSignatureException when the signature or timestamp does not verify.
    PaymentWebhookResult ParseWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody);

    // Reads a payload that ParseWebhook already verified and the inbox stored. The Worker uses it, because the
    // original headers are gone and the signature tolerance may have passed (changelog: section 9.11 extension).
    PaymentWebhookResult ReadStoredEvent(string rawPayload);
}

public enum ProviderPaymentStatus
{
    RequiresPayment,
    Succeeded,
    Failed,
    Canceled,
}

public enum PaymentEventType
{
    Succeeded,
    Failed,
    Refunded,
    Disputed,
    Other,
}

public sealed record CreateProviderPayment(Guid OrderId, Money Amount, string IdempotencyKey);

public sealed record ProviderPayment(string ProviderPaymentId, string? ClientSecret, ProviderPaymentStatus Status, Money Amount);

public sealed record ProviderRefund(string ProviderRefundId, bool WasAlreadyRefunded);

public sealed record PaymentWebhookResult(
    string ProviderEventId, PaymentEventType Type, string? ProviderPaymentId, Money? Amount, DateTimeOffset OccurredAt, string RawPayload);

// The webhook could not be verified: 400 webhook_signature_invalid (section 9.5).
public sealed class WebhookSignatureException : Exception
{
    public WebhookSignatureException()
    {
    }

    public WebhookSignatureException(string message)
        : base(message)
    {
    }

    public WebhookSignatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// A transient provider failure: a timeout, a network error, a 5xx or a 429. Resilience (T-040) retries these,
// and once retries are exhausted the request fails with 503 payment_provider_unavailable.
public sealed class PaymentProviderUnavailableException : Exception
{
    public PaymentProviderUnavailableException()
    {
    }

    public PaymentProviderUnavailableException(string message)
        : base(message)
    {
    }

    public PaymentProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
