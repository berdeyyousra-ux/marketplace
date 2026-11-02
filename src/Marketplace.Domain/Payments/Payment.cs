using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public enum PaymentStatus
{
    RequiresPayment,
    Succeeded,
    Failed,
    Refunded,
}

public sealed class Payment : AggregateRoot
{
    public const int ProviderMaxLength = 30;
    public const int ProviderPaymentIdMaxLength = 100;
    public const int FailureCodeMaxLength = 100;

    private Payment(Guid orderId, string provider, string providerPaymentId, Money amount, DateTimeOffset now)
    {
        OrderId = orderId;
        Provider = provider;
        ProviderPaymentId = providerPaymentId;
        Amount = amount;
        Status = PaymentStatus.RequiresPayment;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OrderId { get; private set; }

    public string Provider { get; private set; }

    public string ProviderPaymentId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Payment> Create(
        Guid orderId, string provider, string providerPaymentId, Money amount, DateTimeOffset now)
    {
        if (orderId == Guid.Empty)
        {
            return DomainErrors.Validation("Order is required.");
        }

        if (string.IsNullOrWhiteSpace(provider) || provider.Length > ProviderMaxLength)
        {
            return DomainErrors.Validation($"Provider must be 1 to {ProviderMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > ProviderPaymentIdMaxLength)
        {
            return DomainErrors.Validation($"Provider payment ID must be 1 to {ProviderPaymentIdMaxLength} characters.");
        }

        if (amount.Amount <= 0)
        {
            return DomainErrors.Validation("Payment amount must be greater than zero.");
        }

        return new Payment(orderId, provider, providerPaymentId, amount, now);
    }

    public Result MarkSucceeded(DateTimeOffset now) =>
        Move(PaymentStatus.RequiresPayment, PaymentStatus.Succeeded, now);

    public Result MarkFailed(string? failureCode, DateTimeOffset now)
    {
        var result = Move(PaymentStatus.RequiresPayment, PaymentStatus.Failed, now);
        if (result.IsSuccess)
        {
            // Provider codes are recorded for diagnosis, so an overlong one is shortened rather than rejected.
            FailureCode = failureCode is { Length: > FailureCodeMaxLength } ? failureCode[..FailureCodeMaxLength] : failureCode;
        }

        return result;
    }

    public Result MarkRefunded(DateTimeOffset now) =>
        Move(PaymentStatus.Succeeded, PaymentStatus.Refunded, now);

    private Result Move(PaymentStatus requiredFrom, PaymentStatus to, DateTimeOffset now)
    {
        if (Status != requiredFrom)
        {
            return DomainErrors.InvalidStateTransition(nameof(Payment), Status, to);
        }

        Status = to;
        UpdatedAt = now;
        return Result.Success();
    }
}
