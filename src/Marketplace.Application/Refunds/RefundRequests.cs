using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Refunds;

// D-39 and section 16: Refunds:WindowDays.
public sealed class RefundOptions
{
    public const string SectionName = "Refunds";

    public int WindowDays { get; set; } = 14;
}

// Section 12.6: Ai:DailyRequestCap. Beyond it a Skipped suggestion is stored instead of calling the model.
public sealed class AiQuotaOptions
{
    public const string SectionName = "Ai";

    public int DailyRequestCap { get; set; } = 200;
}

// EP-60, EP-61 and US-28.
public sealed record CreateRefundRequestCommand(Guid OrderItemId, string Reason);

public sealed record RefundRequestResponse(Guid Id, Guid OrderItemId, string Status, string PolicyVerdict, DateTimeOffset CreatedAt);

public sealed record MyRefundRequestsQuery(int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record MyRefundRequest(
    Guid Id, Guid OrderItemId, string ProductTitle, string Status, string? Decision, string? DecisionNote, DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt, DateTimeOffset? ExecutedAt);

public sealed record RefundRequestMessage(Guid RefundRequestId);

public static class RefundErrors
{
    public static readonly Error ItemNotFound = new("not_found", "Order item not found.", ErrorType.NotFound);
}

public sealed class CreateRefundRequestCommandValidator : AbstractValidator<CreateRefundRequestCommand>
{
    public CreateRefundRequestCommandValidator()
    {
        RuleFor(command => command.OrderItemId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().Length(RefundRequest.ReasonMinLength, RefundRequest.ReasonMaxLength);
    }
}

public sealed class MyRefundRequestsQueryValidator : AbstractValidator<MyRefundRequestsQuery>
{
    public MyRefundRequestsQueryValidator() => RuleFor(query => query.Limit).ValidLimit();
}

// Section 9.8 request: the policy decides whether the request is accepted; the model is asked later, in the Worker.
public sealed class CreateRefundRequestCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider, IOptions<RefundOptions> options)
    : ICommandHandler<CreateRefundRequestCommand, RefundRequestResponse>
{
    public async Task<Result<RefundRequestResponse>> HandleAsync(CreateRefundRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Requesting a refund requires an authenticated caller.");
        var item = await db.OrderItems.SingleOrDefaultAsync(candidate => candidate.Id == command.OrderItemId, cancellationToken);
        var order = item is null ? null : await db.Orders.SingleOrDefaultAsync(candidate => candidate.Id == item.OrderId && candidate.BuyerId == buyerId, cancellationToken);
        if (item is null || order is null)
        {
            return RefundErrors.ItemNotFound;
        }

        var downloads = await db.DownloadGrants.Where(grant => grant.OrderItemId == item.Id).Select(grant => grant.DownloadCount).SingleOrDefaultAsync(cancellationToken);
        var exists = await db.RefundRequests.AnyAsync(request => request.OrderItemId == item.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var verdict = RefundPolicy.Evaluate(item, order, downloads, exists, now, options.Value.WindowDays);

        var submitted = RefundRequest.Submit(item.Id, buyerId, command.Reason, verdict, downloads, now);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var request = submitted.Value;
        db.RefundRequests.Add(request);
        audit.Record("refund.requested", AuditActorType.User, buyerId, "RefundRequest", request.Id, new { orderItemId = item.Id, verdict = verdict.ToString() });
        outbox.Enqueue(OutboxMessageTypes.AnalyzeRefundRequest, new RefundRequestMessage(request.Id));

        // ux_refund_requests_order_item_id stops a concurrent duplicate; the translator returns it as a 409.
        await db.SaveChangesAsync(cancellationToken);
        return new RefundRequestResponse(request.Id, item.Id, request.Status.ToString(), verdict.ToString(), request.CreatedAt);
    }
}

// The caller's own requests, newest first, keyset on (created_at, id).
public sealed class MyRefundRequestsQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<MyRefundRequestsQuery, Page<MyRefundRequest>>
{
    public async Task<Result<Page<MyRefundRequest>>> HandleAsync(MyRefundRequestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<CreatedCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Listing refund requests requires an authenticated caller.");
        var requests = db.RefundRequests.AsNoTracking().Where(request => request.RequestedBy == buyerId);
        if (cursor is not null)
        {
            requests = requests.Where(request => request.CreatedAt < cursor.CreatedAt || (request.CreatedAt == cursor.CreatedAt && request.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await requests
            .OrderByDescending(request => request.CreatedAt).ThenByDescending(request => request.Id)
            .Take(query.Limit + 1)
            .Join(db.OrderItems, request => request.OrderItemId, item => item.Id, (request, item) => new { Request = request, item.ProductTitle })
            .ToListAsync(cancellationToken);

        var items = rows
            .OrderByDescending(row => row.Request.CreatedAt).ThenByDescending(row => row.Request.Id)
            .Select(row => new MyRefundRequest(
                row.Request.Id, row.Request.OrderItemId, row.ProductTitle, row.Request.Status.ToString(), row.Request.Decision?.ToString(),
                row.Request.DecisionNote, row.Request.CreatedAt, row.Request.DecidedAt, row.Request.ExecutedAt))
            .ToList();
        return Paging.ToPage(items, query.Limit, item => new CreatedCursor(item.CreatedAt, item.Id));
    }
}

// Section 9.8 analyze. Never throws for a model failure: every outcome is stored, and the admin sees that no
// suggestion is available (FM-16). The model's answer is only ever stored (FM-18): no state changes from it.
public sealed class AnalyzeRefundRequestHandler(
    IAppDbContext db, IRefundAdvisor advisor, AuditTrail audit, TimeProvider timeProvider, IOptions<RefundOptions> refundOptions,
    IOptions<AiQuotaOptions> quota)
    : IOutboxHandler
{
    public const int MaxBuyerMessageLength = 2000;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.AnalyzeRefundRequest;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var requestId = (JsonSerializer.Deserialize<RefundRequestMessage>(payload, _json) ?? throw new JsonException("Missing refundRequestId.")).RefundRequestId;
        var request = await db.RefundRequests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        if (request is null
            || await db.AiSuggestions.AnyAsync(suggestion => suggestion.RefundRequestId == requestId && suggestion.ValidationStatus == AiSuggestionStatus.Valid, cancellationToken))
        {
            return;
        }

        var facts = await BuildFactsAsync(request, cancellationToken);
        var factsJson = JsonSerializer.Serialize(facts, _json);
        var now = timeProvider.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var usedToday = await db.AiSuggestions.CountAsync(
            suggestion => suggestion.CreatedAt >= dayStart && suggestion.ValidationStatus != AiSuggestionStatus.Skipped, cancellationToken);

        var result = usedToday >= quota.Value.DailyRequestCap
            ? new AdvisorResult(AdvisorStatus.Skipped, null, null, "none", "n/a", null, null, 0, "The daily AI request cap is reached.")
            : await advisor.AnalyzeAsync(facts, cancellationToken);

        var status = result.Status switch
        {
            AdvisorStatus.Valid => AiSuggestionStatus.Valid,
            AdvisorStatus.InvalidOutput => AiSuggestionStatus.InvalidOutput,
            AdvisorStatus.ProviderError => AiSuggestionStatus.ProviderError,
            _ => AiSuggestionStatus.Skipped,
        };
        db.AiSuggestions.Add(AiSuggestion.Record(requestId, result.Model, result.PromptVersion, factsJson, status, timeProvider.GetUtcNow())
            .WithOutcome(
                result.RawOutput,
                result.Suggestion is null ? null : JsonSerializer.Serialize(result.Suggestion, _json),
                result.InputTokens,
                result.OutputTokens,
                result.LatencyMs,
                result.Error));
        audit.Record("refund.ai_analyzed", AuditActorType.Worker, entityType: "RefundRequest", entityId: requestId, details: new { status = status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
    }

    // Section 12.2, as at the time of the request. No names, emails or payment details.
    private async Task<RefundFacts> BuildFactsAsync(RefundRequest request, CancellationToken cancellationToken)
    {
        var item = await db.OrderItems.AsNoTracking().SingleAsync(candidate => candidate.Id == request.OrderItemId, cancellationToken);
        var paidAt = await db.Orders.Where(order => order.Id == item.OrderId).Select(order => order.PaidAt).SingleAsync(cancellationToken)
            ?? request.CreatedAt;
        var categorySlug = await db.Products.Where(product => product.Id == item.ProductId)
            .Join(db.Categories, product => product.CategoryId, category => category.Id, (product, category) => category.Slug)
            .SingleOrDefaultAsync(cancellationToken) ?? "other";

        var buyerSince = request.CreatedAt.AddDays(-180);
        var buyerRefunds = await db.OrderItems
            .Where(candidate => candidate.Status == OrderItemStatus.Refunded && candidate.RefundedAt >= buyerSince)
            .Join(db.Orders.Where(order => order.BuyerId == request.RequestedBy), candidate => candidate.OrderId, order => order.Id, (candidate, order) => candidate.Id)
            .CountAsync(cancellationToken);

        var sellerSince = request.CreatedAt.AddDays(-90);
        var sellerItems = db.OrderItems.Where(candidate => candidate.SellerId == item.SellerId)
            .Join(db.Orders.Where(order => order.PaidAt >= sellerSince), candidate => candidate.OrderId, order => order.Id, (candidate, order) => candidate.Status);
        var sellerPaid = await sellerItems.CountAsync(cancellationToken);
        var sellerRefunded = await sellerItems.CountAsync(status => status == OrderItemStatus.Refunded, cancellationToken);

        return new RefundFacts(
            request.Id,
            CleanBuyerMessage(request.ReasonText),
            item.ProductTitle,
            categorySlug,
            string.Create(CultureInfo.InvariantCulture, $"{item.UnitPrice.Amount:0.00} {item.UnitPrice.Currency}"),
            (int)Math.Floor((request.CreatedAt - paidAt).TotalDays),
            refundOptions.Value.WindowDays,
            request.PolicyVerdict != RefundPolicyVerdict.IneligibleOutsideWindow,
            request.DownloadCountAtRequest,
            buyerRefunds,
            sellerPaid == 0 ? 0m : Math.Round((decimal)sellerRefunded / sellerPaid, 4, MidpointRounding.AwayFromZero),
            request.PolicyVerdict.ToString());
    }

    // Control characters are removed (line breaks and tabs are kept) and the text is capped at 2000 characters.
    public static string CleanBuyerMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var cleaned = new StringBuilder(Math.Min(text.Length, MaxBuyerMessageLength));
        foreach (var character in text.Where(character => !char.IsControl(character) || character is '\n' or '\t'))
        {
            if (cleaned.Length == MaxBuyerMessageLength)
            {
                break;
            }

            cleaned.Append(character);
        }

        return cleaned.ToString();
    }
}
