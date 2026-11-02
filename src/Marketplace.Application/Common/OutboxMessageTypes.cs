namespace Marketplace.Application.Common;

// The section 9.10 outbox message catalogue.
public static class OutboxMessageTypes
{
    public const string EmailVerificationRequested = nameof(EmailVerificationRequested);
    public const string PasswordResetRequested = nameof(PasswordResetRequested);
    public const string AccountExistsNotice = nameof(AccountExistsNotice);
    public const string OrderReceiptEmail = nameof(OrderReceiptEmail);
    public const string ProcessPaymentEvent = nameof(ProcessPaymentEvent);
    public const string AnalyzeRefundRequest = nameof(AnalyzeRefundRequest);
    public const string ExecuteRefund = nameof(ExecuteRefund);
    public const string RefundDecisionEmail = nameof(RefundDecisionEmail);
    public const string RefundCompletedEmail = nameof(RefundCompletedEmail);
    public const string AutoRefundPayment = nameof(AutoRefundPayment);
    public const string CancelPaymentIntent = nameof(CancelPaymentIntent);
    public const string DeleteBlob = nameof(DeleteBlob);
}

// Payload for the user-addressed messages. Raw tokens are never put in the outbox (section 9.1).
public sealed record UserMessage(Guid UserId);
