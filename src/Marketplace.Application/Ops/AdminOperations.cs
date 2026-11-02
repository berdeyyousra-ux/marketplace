using System.Text.Json;
using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Ops;

// EP-80 to EP-83, US-32 and US-34. Admin only.
public sealed record RemoveProductRequest(string Reason);

public sealed record RemoveProductCommand(Guid ProductId, string Reason);

public sealed record AuditLogQuery(
    string? EntityType = null, Guid? EntityId = null, Guid? ActorUserId = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record AuditLogItem(
    long Id, DateTimeOffset OccurredAt, string ActorType, Guid? ActorUserId, string Action, string? EntityType, Guid? EntityId, JsonElement? Details,
    string? CorrelationId);

public sealed record AuditCursor(DateTimeOffset OccurredAt, long Id);

public sealed record DeadLettersQuery(int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record DeadLetter(
    Guid Id, string Type, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset DeadLetteredAt, string? CorrelationId);

public sealed record DeadLetterCursor(DateTimeOffset DeadLetteredAt, Guid Id);

public sealed record RetryOutboxCommand(Guid MessageId);

public static class OpsErrors
{
    public static readonly Error MessageNotFound = new("not_found", "Outbox message not found.", ErrorType.NotFound);

    public static readonly Error NotDeadLettered = DomainErrors.InvalidState("Only a dead-lettered message can be retried.");
}

public sealed class RemoveProductRequestValidator : AbstractValidator<RemoveProductRequest>
{
    public RemoveProductRequestValidator() => RuleFor(request => request.Reason).NotEmpty().MaximumLength(Product.RemovedReasonMaxLength);
}

public sealed class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    public AuditLogQueryValidator()
    {
        RuleFor(query => query.Limit).ValidLimit();
        RuleFor(query => query.EntityType).MaximumLength(50);
    }
}

public sealed class DeadLettersQueryValidator : AbstractValidator<DeadLettersQuery>
{
    public DeadLettersQueryValidator() => RuleFor(query => query.Limit).ValidLimit();
}

// US-32: the product leaves every public list. Download grants are untouched, so buyers keep access.
public sealed class RemoveProductCommandHandler(IAppDbContext db, ICurrentUser currentUser, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<RemoveProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(RemoveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await db.Products.SingleOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var removed = product.Remove(command.Reason, timeProvider.GetUtcNow());
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        audit.Record("product.removed", AuditActorType.User, currentUser.UserId, "Product", product.Id, new { reason = command.Reason });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DomainErrors.InvalidState("The product changed while it was being removed. Try again.");
        }

        return ProductMapping.ToResponse(product, isOwner: false, showFile: true);
    }
}

// Newest first, keyset on (occurred_at, id). ix_audit_log_entity serves the entity filter, ix_audit_log_occurred_at
// the rest.
public sealed class AuditLogQueryHandler(IAppDbContext db) : IQueryHandler<AuditLogQuery, Page<AuditLogItem>>
{
    public async Task<Result<Page<AuditLogItem>>> HandleAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<AuditCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var entries = db.AuditLog.AsNoTracking();
        if (query.EntityType is not null)
        {
            entries = entries.Where(entry => entry.EntityType == query.EntityType);
        }

        if (query.EntityId is not null)
        {
            entries = entries.Where(entry => entry.EntityId == query.EntityId);
        }

        if (query.ActorUserId is not null)
        {
            entries = entries.Where(entry => entry.ActorUserId == query.ActorUserId);
        }

        if (query.From is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt >= query.From);
        }

        if (query.To is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt < query.To);
        }

        if (cursor is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt < cursor.OccurredAt || (entry.OccurredAt == cursor.OccurredAt && entry.Id < cursor.Id));
        }

        var rows = await entries
            .OrderByDescending(entry => entry.OccurredAt).ThenByDescending(entry => entry.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var items = rows.ConvertAll(entry => new AuditLogItem(
            entry.Id, entry.OccurredAt, entry.ActorType.ToString(), entry.ActorUserId, entry.Action, entry.EntityType, entry.EntityId,
            entry.Details is null ? null : JsonSerializer.Deserialize<JsonElement>(entry.Details), entry.CorrelationId));
        return Paging.ToPage(items, query.Limit, item => new AuditCursor(item.OccurredAt, item.Id));
    }
}

// Most recently dead-lettered first. The payload is left out: the type, error and correlation id are enough to
// find the cause, and the message is retried as it is.
public sealed class DeadLettersQueryHandler(IAppDbContext db) : IQueryHandler<DeadLettersQuery, Page<DeadLetter>>
{
    public async Task<Result<Page<DeadLetter>>> HandleAsync(DeadLettersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<DeadLetterCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var messages = db.OutboxMessages.AsNoTracking().Where(message => message.DeadLetteredAt != null);
        if (cursor is not null)
        {
            messages = messages.Where(message => message.DeadLetteredAt < cursor.DeadLetteredAt
                || (message.DeadLetteredAt == cursor.DeadLetteredAt && message.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await messages
            .OrderByDescending(message => message.DeadLetteredAt).ThenByDescending(message => message.Id)
            .Take(query.Limit + 1)
            .Select(message => new DeadLetter(
                message.Id, message.Type, message.Attempts, message.LastError, message.CreatedAt, message.DeadLetteredAt!.Value, message.CorrelationId))
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, query.Limit, row => new DeadLetterCursor(row.DeadLetteredAt, row.Id));
    }
}

// EP-83: clears the dead letter and resets the attempts, so the Worker claims it again at once.
public sealed class RetryOutboxCommandHandler(IAppDbContext db, ICurrentUser currentUser, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<RetryOutboxCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RetryOutboxCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var updated = await db.OutboxMessages
            .Where(message => message.Id == command.MessageId && message.DeadLetteredAt != null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(message => message.DeadLetteredAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.Attempts, 0)
                    .SetProperty(message => message.AvailableAt, now),
                cancellationToken);
        if (updated == 0)
        {
            return await db.OutboxMessages.AnyAsync(message => message.Id == command.MessageId, cancellationToken)
                ? OpsErrors.NotDeadLettered
                : OpsErrors.MessageNotFound;
        }

        audit.Record("outbox.retried", AuditActorType.User, currentUser.UserId, "OutboxMessage", command.MessageId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
