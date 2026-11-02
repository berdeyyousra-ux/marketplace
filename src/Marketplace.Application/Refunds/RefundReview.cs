using System.Text.Json;
using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Refunds;

// EP-62 to EP-65, US-30 and US-31.
public sealed record AdminRefundRequestsQuery(string? Status = null, int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record AiSummary(string Status, string? Category, string? RecommendedAction, string? Summary, IReadOnlyList<string> Flags);

public sealed record AdminRefundSummary(
    Guid Id, Guid OrderItemId, string ProductTitle, string Status, string PolicyVerdict, int DownloadCount, DateTimeOffset CreatedAt, AiSummary? Ai);

public sealed record AdminRefundRequestQuery(Guid RefundRequestId);

public sealed record AiSuggestionDetail(
    string Status, string Model, string PromptVersion, JsonElement Facts, JsonElement? Suggestion, string? Error, int? LatencyMs, DateTimeOffset CreatedAt);

public sealed record RefundHistoryEntry(string Action, string ActorType, DateTimeOffset OccurredAt);

public sealed record AdminRefundDetail(
    Guid Id, Guid OrderItemId, Guid OrderId, Guid ProductId, string ProductTitle, MoneyDto UnitPrice, string BuyerMessage, string Status,
    string PolicyVerdict, int DownloadCountAtRequest, DateTimeOffset CreatedAt, string? Decision, string? DecisionNote, string? OverrideReason,
    DateTimeOffset? DecidedAt, DateTimeOffset? ExecutedAt, string? FailureReason, AiSuggestionDetail? AiSuggestion, IReadOnlyList<RefundHistoryEntry> History);

public sealed record RefundDecisionRequest(string Decision, string Note, string? OverrideReason);

public sealed record DecideRefundCommand(Guid RefundRequestId, string Decision, string Note, string? OverrideReason);

public sealed record RefundDecisionResponse(Guid Id, string Status, string Decision, DateTimeOffset DecidedAt);

public sealed record RetryRefundCommand(Guid RefundRequestId);

public static class RefundReviewErrors
{
    public static readonly Error NotFound = new("not_found", "Refund request not found.", ErrorType.NotFound);
}

public sealed class AdminRefundRequestsQueryValidator : AbstractValidator<AdminRefundRequestsQuery>
{
    public AdminRefundRequestsQueryValidator()
    {
        RuleFor(query => query.Limit).ValidLimit();
        RuleFor(query => query.Status).Must(status => status is null || Enum.TryParse<RefundRequestStatus>(status, ignoreCase: false, out _))
            .WithMessage("Status must be a refund request status, such as Submitted or Failed.");
    }
}

public sealed class RefundDecisionRequestValidator : AbstractValidator<RefundDecisionRequest>
{
    public RefundDecisionRequestValidator()
    {
        RuleFor(request => request.Decision).Must(decision => decision is nameof(RefundDecision.Approved) or nameof(RefundDecision.Rejected))
            .WithMessage("Decision must be Approved or Rejected.");
        RuleFor(request => request.Note).NotEmpty().MaximumLength(RefundRequest.NoteMaxLength);
        RuleFor(request => request.OverrideReason).MaximumLength(RefundRequest.NoteMaxLength);
    }
}

internal static class SuggestionReader
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static AiSummary? Summarize(AiSuggestionStatus? status, string? parsedOutput)
    {
        if (status is null)
        {
            return null;
        }

        var suggestion = parsedOutput is null ? null : JsonSerializer.Deserialize<RefundSuggestion>(parsedOutput, _json);
        return new AiSummary(status.Value.ToString(), suggestion?.Category, suggestion?.RecommendedAction, suggestion?.Summary, suggestion?.Flags ?? []);
    }
}

// US-30: oldest first, so the queue is worked in order; keyset on (created_at, id). Each row carries the latest
// suggestion, or none.
public sealed class AdminRefundRequestsQueryHandler(IAppDbContext db) : IQueryHandler<AdminRefundRequestsQuery, Page<AdminRefundSummary>>
{
    public async Task<Result<Page<AdminRefundSummary>>> HandleAsync(AdminRefundRequestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<CreatedCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var requests = db.RefundRequests.AsNoTracking();
        if (query.Status is not null)
        {
            var status = Enum.Parse<RefundRequestStatus>(query.Status);
            requests = requests.Where(request => request.Status == status);
        }

        if (cursor is not null)
        {
            requests = requests.Where(request => request.CreatedAt > cursor.CreatedAt || (request.CreatedAt == cursor.CreatedAt && request.Id.CompareTo(cursor.Id) > 0));
        }

        var rows = await requests
            .OrderBy(request => request.CreatedAt).ThenBy(request => request.Id)
            .Take(query.Limit + 1)
            .Select(request => new
            {
                Request = request,
                Title = db.OrderItems.Where(item => item.Id == request.OrderItemId).Select(item => item.ProductTitle).Single(),
                Latest = db.AiSuggestions.Where(suggestion => suggestion.RefundRequestId == request.Id)
                    .OrderByDescending(suggestion => suggestion.CreatedAt)
                    .Select(suggestion => new { suggestion.ValidationStatus, suggestion.ParsedOutput })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.ConvertAll(row => new AdminRefundSummary(
            row.Request.Id, row.Request.OrderItemId, row.Title, row.Request.Status.ToString(), row.Request.PolicyVerdict.ToString(),
            row.Request.DownloadCountAtRequest, row.Request.CreatedAt,
            SuggestionReader.Summarize(row.Latest?.ValidationStatus, row.Latest?.ParsedOutput)));
        return Paging.ToPage(items, query.Limit, item => new CreatedCursor(item.CreatedAt, item.Id));
    }
}

// Section 12.6: the facts sent, the verdict, the suggestion with its flags, the model and prompt version, or a
// clear absence. History is the request's audit trail.
public sealed class AdminRefundRequestQueryHandler(IAppDbContext db) : IQueryHandler<AdminRefundRequestQuery, AdminRefundDetail>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<Result<AdminRefundDetail>> HandleAsync(AdminRefundRequestQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var request = await db.RefundRequests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.RefundRequestId, cancellationToken);
        if (request is null)
        {
            return RefundReviewErrors.NotFound;
        }

        var item = await db.OrderItems.AsNoTracking().SingleAsync(candidate => candidate.Id == request.OrderItemId, cancellationToken);
        var suggestion = await db.AiSuggestions.AsNoTracking()
            .Where(candidate => candidate.RefundRequestId == request.Id)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var history = await db.AuditLog.AsNoTracking()
            .Where(entry => entry.EntityType == "RefundRequest" && entry.EntityId == request.Id)
            .OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id)
            .Select(entry => new RefundHistoryEntry(entry.Action, entry.ActorType.ToString(), entry.OccurredAt))
            .ToListAsync(cancellationToken);

        return new AdminRefundDetail(
            request.Id, item.Id, item.OrderId, item.ProductId, item.ProductTitle, MoneyDto.From(item.UnitPrice), request.ReasonText, request.Status.ToString(),
            request.PolicyVerdict.ToString(), request.DownloadCountAtRequest, request.CreatedAt, request.Decision?.ToString(), request.DecisionNote,
            request.OverrideReason, request.DecidedAt, request.ExecutedAt, request.FailureReason,
            suggestion is null
                ? null
                : new AiSuggestionDetail(
                    suggestion.ValidationStatus.ToString(), suggestion.Model, suggestion.PromptVersion,
                    JsonSerializer.Deserialize<JsonElement>(suggestion.InputFacts, _json),
                    suggestion.ParsedOutput is null ? null : JsonSerializer.Deserialize<JsonElement>(suggestion.ParsedOutput, _json),
                    suggestion.Error, suggestion.LatencyMs, suggestion.CreatedAt),
            history);
    }
}

// Section 9.8 decide. The xmin token makes the Submitted check conditional: of two admins deciding at once, one
// wins and the other gets refund_already_decided (FM-20).
public sealed class DecideRefundCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<DecideRefundCommand, RefundDecisionResponse>
{
    public async Task<Result<RefundDecisionResponse>> HandleAsync(DecideRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var adminId = currentUser.UserId ?? throw new InvalidOperationException("Deciding requires an authenticated admin.");
        var request = await db.RefundRequests.SingleOrDefaultAsync(candidate => candidate.Id == command.RefundRequestId, cancellationToken);
        if (request is null)
        {
            return RefundReviewErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var approve = command.Decision == nameof(RefundDecision.Approved);
        var decided = approve ? request.Approve(adminId, command.Note, command.OverrideReason, now) : request.Reject(adminId, command.Note, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        audit.Record(
            approve ? "refund.approved" : "refund.rejected", AuditActorType.User, adminId, "RefundRequest", request.Id,
            new { overridden = request.OverrideReason is not null });
        if (approve)
        {
            outbox.Enqueue(OutboxMessageTypes.ExecuteRefund, new RefundRequestMessage(request.Id));
        }

        outbox.Enqueue(OutboxMessageTypes.RefundDecisionEmail, new RefundRequestMessage(request.Id));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RefundRequest.AlreadyDecided;
        }

        return new RefundDecisionResponse(request.Id, request.Status.ToString(), request.Decision!.Value.ToString(), request.DecidedAt!.Value);
    }
}

// EP-65 and FM-12: a Failed request goes back to Executing and is queued again.
public sealed class RetryRefundCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<RetryRefundCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RetryRefundCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = await db.RefundRequests.SingleOrDefaultAsync(candidate => candidate.Id == command.RefundRequestId, cancellationToken);
        if (request is null)
        {
            return RefundReviewErrors.NotFound;
        }

        if (request.Status != RefundRequestStatus.Failed)
        {
            return DomainErrors.InvalidState("Only a Failed refund can be retried.");
        }

        var started = request.StartExecution(timeProvider.GetUtcNow());
        if (started.IsFailure)
        {
            return started.Error;
        }

        audit.Record("refund.retried", AuditActorType.User, currentUser.UserId, "RefundRequest", request.Id);
        outbox.Enqueue(OutboxMessageTypes.ExecuteRefund, new RefundRequestMessage(request.Id));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DomainErrors.InvalidState("The refund changed while it was being retried.");
        }

        return Unit.Value;
    }
}

// Section 9.8 execute. Transaction 1 marks the request Executing; the provider call runs outside any transaction
// with the idempotency key refund-{id}; transaction 2 applies every effect. A provider failure throws, so the
// outbox retries it; once dead-lettered, OnDeadLetterAsync marks the request Failed (FM-12).
public sealed class ExecuteRefundHandler(
    IAppDbContext db, IPaymentProvider provider, IRowLocks locks, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.ExecuteRefund;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var requestId = ReadId(payload);
        var request = await db.RefundRequests.SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        if (request is null || request.Status is not (RefundRequestStatus.Approved or RefundRequestStatus.Executing))
        {
            return;
        }

        if (request.Status == RefundRequestStatus.Approved)
        {
            Require(request.StartExecution(timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }

        var item = await db.OrderItems.AsNoTracking().SingleAsync(candidate => candidate.Id == request.OrderItemId, cancellationToken);
        var payment = await db.Payments.AsNoTracking()
            .Where(candidate => candidate.OrderId == item.OrderId && (candidate.Status == PaymentStatus.Succeeded || candidate.Status == PaymentStatus.Refunded))
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NonRetryableException($"Order {item.OrderId} has no successful payment to refund.");

        // A provider answer of already-refunded counts as success (section 9.8).
        await provider.RefundAsync(payment.ProviderPaymentId, item.UnitPrice, $"refund-{request.Id}", cancellationToken);

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(item.OrderId, cancellationToken);
        var order = await db.Orders.Include(candidate => candidate.Items).SingleAsync(candidate => candidate.Id == item.OrderId, cancellationToken);
        var refundedItem = order.Items.Single(candidate => candidate.Id == item.Id);
        Require(request.Complete(now));
        Require(order.MarkItemRefunded(item.Id, now));
        if (order.Items.All(candidate => candidate.Status == Domain.Orders.OrderItemStatus.Refunded))
        {
            var trackedPayment = await db.Payments.SingleAsync(candidate => candidate.Id == payment.Id, cancellationToken);
            if (trackedPayment.Status == PaymentStatus.Succeeded)
            {
                Require(trackedPayment.MarkRefunded(now));
            }
        }

        var grant = await db.DownloadGrants.SingleOrDefaultAsync(candidate => candidate.OrderItemId == item.Id, cancellationToken);
        grant?.Revoke(now);
        db.LedgerEntries.AddRange(LedgerEntry.ForRefundedItem(refundedItem, now));
        audit.Record("refund.executed", AuditActorType.Worker, entityType: "RefundRequest", entityId: request.Id, details: new { amount = item.UnitPrice.Amount });
        outbox.Enqueue(OutboxMessageTypes.RefundCompletedEmail, new RefundRequestMessage(request.Id));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task OnDeadLetterAsync(string payload, string reason, CancellationToken cancellationToken)
    {
        var request = await db.RefundRequests.SingleOrDefaultAsync(candidate => candidate.Id == ReadId(payload), cancellationToken);
        if (request is not null && request.Fail(reason, timeProvider.GetUtcNow()).IsSuccess)
        {
            audit.Record("refund.failed", AuditActorType.Worker, entityType: "RefundRequest", entityId: request.Id);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static Guid ReadId(string payload) =>
        (JsonSerializer.Deserialize<RefundRequestMessage>(payload, _json) ?? throw new JsonException("Missing refundRequestId.")).RefundRequestId;

    private static void Require(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.Message);
        }
    }
}

// Section 9.10: RefundDecisionEmail and RefundCompletedEmail. Both re-check that the request and buyer exist.
public abstract class RefundEmailHandler(IAppDbContext db, IIdentityService identity, IEmailSender email) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public abstract string MessageType { get; }

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var requestId = (JsonSerializer.Deserialize<RefundRequestMessage>(payload, _json) ?? throw new JsonException("Missing refundRequestId.")).RefundRequestId;
        var request = await db.RefundRequests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        var account = request is null ? null : await identity.GetAccountAsync(request.RequestedBy, cancellationToken);
        if (request is null || account is null)
        {
            return;
        }

        var title = await db.OrderItems.Where(item => item.Id == request.OrderItemId).Select(item => item.ProductTitle).SingleAsync(cancellationToken);
        var message = Compose(request, title, account.DisplayName);
        if (message is not null)
        {
            await email.SendAsync(new EmailMessage(account.Email, message.Value.Subject, message.Value.Body), cancellationToken);
        }
    }

    protected abstract (string Subject, string Body)? Compose(RefundRequest request, string productTitle, string displayName);
}

public sealed class RefundDecisionEmailHandler(IAppDbContext db, IIdentityService identity, IEmailSender email) : RefundEmailHandler(db, identity, email)
{
    public override string MessageType => OutboxMessageTypes.RefundDecisionEmail;

    protected override (string Subject, string Body)? Compose(RefundRequest request, string productTitle, string displayName) =>
        request.Decision switch
        {
            RefundDecision.Approved => ($"Your refund for {productTitle} is approved",
                $"Hi {displayName},\n\nYour refund request for {productTitle} was approved. We are processing it now and will email you when it is complete.\n\nNote from our team: {request.DecisionNote}"),
            RefundDecision.Rejected => ($"Your refund request for {productTitle}",
                $"Hi {displayName},\n\nYour refund request for {productTitle} was not approved.\n\nNote from our team: {request.DecisionNote}"),
            _ => null,
        };
}

public sealed class RefundCompletedEmailHandler(IAppDbContext db, IIdentityService identity, IEmailSender email) : RefundEmailHandler(db, identity, email)
{
    public override string MessageType => OutboxMessageTypes.RefundCompletedEmail;

    protected override (string Subject, string Body)? Compose(RefundRequest request, string productTitle, string displayName) =>
        request.Status == RefundRequestStatus.Completed
            ? ($"Your refund for {productTitle} is complete",
                $"Hi {displayName},\n\nYour refund for {productTitle} is complete. It can take a few days to appear on your statement. Access to the download has ended.")
            : null;
}
