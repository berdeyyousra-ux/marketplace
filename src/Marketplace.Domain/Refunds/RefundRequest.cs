using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public enum RefundRequestStatus
{
    Submitted,
    Approved,
    Rejected,
    Executing,
    Completed,
    Failed,
}

public enum RefundDecision
{
    Approved,
    Rejected,
}

public sealed class RefundRequest : AggregateRoot
{
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 2000;
    public const int NoteMaxLength = 1000;
    public const int FailureReasonMaxLength = 500;

    public static readonly Error NotAllowed =
        new("refund_not_allowed", "This item cannot be refunded.", ErrorType.Conflict);

    public static readonly Error AlreadyDecided =
        new("refund_already_decided", "A decision has already been made on this refund request.", ErrorType.Conflict);

    public static readonly Error OverrideReasonRequired =
        new("override_reason_required", "Approving a request outside the refund window needs an override reason.", ErrorType.Validation);

    private RefundRequest(
        Guid orderItemId, Guid requestedBy, string reasonText, RefundPolicyVerdict verdict, int downloadCount, DateTimeOffset now)
    {
        OrderItemId = orderItemId;
        RequestedBy = requestedBy;
        ReasonText = reasonText;
        PolicyVerdict = verdict;
        DownloadCountAtRequest = downloadCount;
        Status = RefundRequestStatus.Submitted;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // Used by EF Core when materializing from the database.
    private RefundRequest()
    {
        ReasonText = string.Empty;
    }

    public Guid OrderItemId { get; private set; }

    public Guid RequestedBy { get; private set; }

    public string ReasonText { get; private set; }

    public RefundRequestStatus Status { get; private set; }

    public RefundPolicyVerdict PolicyVerdict { get; private set; }

    public int DownloadCountAtRequest { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public RefundDecision? Decision { get; private set; }

    public string? DecisionNote { get; private set; }

    public string? OverrideReason { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public DateTimeOffset? ExecutedAt { get; private set; }

    public string? FailureReason { get; private set; }

    // Mapped to the PostgreSQL xmin system column as an optimistic concurrency token (D-15).
    public uint Version { get; private set; }

    public static Result<RefundRequest> Submit(
        Guid orderItemId, Guid requestedBy, string reasonText, RefundPolicyVerdict verdict, int downloadCount, DateTimeOffset now)
    {
        if (!RefundPolicy.AllowsRequest(verdict))
        {
            return NotAllowed;
        }

        if (orderItemId == Guid.Empty || requestedBy == Guid.Empty)
        {
            return DomainErrors.Validation("Order item and requester are required.");
        }

        if (string.IsNullOrWhiteSpace(reasonText) || reasonText.Length is < ReasonMinLength or > ReasonMaxLength)
        {
            return DomainErrors.Validation($"Reason must be {ReasonMinLength} to {ReasonMaxLength} characters.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(downloadCount);
        return new RefundRequest(orderItemId, requestedBy, reasonText, verdict, downloadCount, now);
    }

    public Result Approve(Guid adminId, string note, string? overrideReason, DateTimeOffset now)
    {
        var error = ValidateDecision(adminId, note);
        if (error is not null)
        {
            return error;
        }

        if (overrideReason is { Length: > NoteMaxLength })
        {
            return DomainErrors.Validation($"Override reason must be at most {NoteMaxLength} characters.");
        }

        if (PolicyVerdict == RefundPolicyVerdict.IneligibleOutsideWindow && string.IsNullOrWhiteSpace(overrideReason))
        {
            return OverrideReasonRequired;
        }

        Decide(RefundDecision.Approved, RefundRequestStatus.Approved, adminId, note, now);
        OverrideReason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason;
        return Result.Success();
    }

    public Result Reject(Guid adminId, string note, DateTimeOffset now)
    {
        var error = ValidateDecision(adminId, note);
        if (error is not null)
        {
            return error;
        }

        Decide(RefundDecision.Rejected, RefundRequestStatus.Rejected, adminId, note, now);
        return Result.Success();
    }

    // Approved to Executing by the worker, Failed to Executing by an admin retry (section 6.3).
    public Result StartExecution(DateTimeOffset now) =>
        Status is RefundRequestStatus.Approved or RefundRequestStatus.Failed
            ? Move(RefundRequestStatus.Executing, now)
            : Transition(RefundRequestStatus.Executing);

    public Result Complete(DateTimeOffset now)
    {
        if (Status != RefundRequestStatus.Executing)
        {
            return Transition(RefundRequestStatus.Completed);
        }

        ExecutedAt = now;
        return Move(RefundRequestStatus.Completed, now);
    }

    public Result Fail(string reason, DateTimeOffset now)
    {
        if (Status != RefundRequestStatus.Executing)
        {
            return Transition(RefundRequestStatus.Failed);
        }

        FailureReason = reason is { Length: > FailureReasonMaxLength } ? reason[..FailureReasonMaxLength] : reason;
        return Move(RefundRequestStatus.Failed, now);
    }

    // A repeated decision is refund_already_decided (sections 8.2 and 9.8) rather than invalid_state_transition.
    private Error? ValidateDecision(Guid adminId, string note)
    {
        if (Status != RefundRequestStatus.Submitted)
        {
            return AlreadyDecided;
        }

        if (adminId == Guid.Empty)
        {
            return DomainErrors.Validation("Admin is required.");
        }

        return string.IsNullOrWhiteSpace(note) || note.Length > NoteMaxLength
            ? DomainErrors.Validation($"Decision note must be 1 to {NoteMaxLength} characters.")
            : null;
    }

    private void Decide(RefundDecision decision, RefundRequestStatus status, Guid adminId, string note, DateTimeOffset now)
    {
        Decision = decision;
        DecidedBy = adminId;
        DecisionNote = note;
        DecidedAt = now;
        Move(status, now);
    }

    private Result Move(RefundRequestStatus to, DateTimeOffset now)
    {
        Status = to;
        UpdatedAt = now;
        return Result.Success();
    }

    private Error Transition(RefundRequestStatus to) =>
        DomainErrors.InvalidStateTransition(nameof(RefundRequest), Status, to);
}
