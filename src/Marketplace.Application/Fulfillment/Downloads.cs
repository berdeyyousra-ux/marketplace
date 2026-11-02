using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Fulfillment;

// EP-50, EP-51 and US-26.
public sealed record CreateDownloadLinkCommand(Guid OrderItemId);

public sealed record DownloadLink(Uri Url, DateTimeOffset ExpiresAt);

public sealed record LibraryQuery(int Limit = Paging.DefaultLimit, string? Cursor = null);

// Status is Active, or the reason the grant can no longer be used: Revoked, Expired or Limit.
public sealed record LibraryItem(
    Guid OrderItemId, Guid ProductId, string Title, string? FileName, DateTimeOffset PurchasedAt, DateTimeOffset ExpiresAt,
    int DownloadCount, int MaxDownloads, string Status, Uri? CoverImageUrl);

public static class DownloadErrors
{
    public static readonly Error NotFound = new("not_found", "No download exists for this order item.", ErrorType.NotFound);
}

public sealed class LibraryQueryValidator : AbstractValidator<LibraryQuery>
{
    public LibraryQueryValidator() => RuleFor(query => query.Limit).ValidLimit();
}

// Section 9.7. The count is taken by one conditional UPDATE, so concurrent requests can never exceed the limit.
public sealed class CreateDownloadLinkCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IFileStorage storage, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<CreateDownloadLinkCommand, DownloadLink>
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<Result<DownloadLink>> HandleAsync(CreateDownloadLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Downloading requires an authenticated caller.");
        var grantId = await db.DownloadGrants
            .Where(grant => grant.OrderItemId == command.OrderItemId && grant.BuyerId == buyerId)
            .Select(grant => (Guid?)grant.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (grantId is null)
        {
            return DownloadErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var updated = await db.DownloadGrants
            .Where(grant => grant.Id == grantId && grant.RevokedAt == null && grant.ExpiresAt > now && grant.DownloadCount < grant.MaxDownloads)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.DownloadCount, grant => grant.DownloadCount + 1), cancellationToken);
        if (updated == 0)
        {
            var current = await db.DownloadGrants.AsNoTracking().SingleAsync(grant => grant.Id == grantId, cancellationToken);
            return DownloadGrant.Unavailable(current.GetUnavailableReason(now) ?? DownloadUnavailableReason.Limit);
        }

        // The buyer always receives the product's current file.
        var file = await db.DownloadGrants.AsNoTracking()
            .Where(grant => grant.Id == grantId)
            .Join(db.Products, grant => grant.ProductId, product => product.Id, (grant, product) => product.File)
            .SingleAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The product for grant {grantId} has no file.");

        var expiresAt = now + LinkLifetime;
        var url = await storage.CreateDownloadUrlAsync(file.Key, file.Name, expiresAt, cancellationToken);
        audit.Record("download.link_issued", AuditActorType.User, buyerId, "DownloadGrant", grantId, new { command.OrderItemId });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DownloadLink(url, expiresAt);
    }
}

// Every grant the caller holds, newest purchase first, keyset on (created_at, order_item_id).
public sealed class LibraryQueryHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider, IPublicImageStorage images)
    : IQueryHandler<LibraryQuery, Page<LibraryItem>>
{
    public async Task<Result<Page<LibraryItem>>> HandleAsync(LibraryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<CreatedCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("The library requires an authenticated caller.");
        var grants = db.DownloadGrants.AsNoTracking().Where(grant => grant.BuyerId == buyerId);
        if (cursor is not null)
        {
            grants = grants.Where(grant => grant.CreatedAt < cursor.CreatedAt || (grant.CreatedAt == cursor.CreatedAt && grant.OrderItemId.CompareTo(cursor.Id) < 0));
        }

        var rows = await grants
            .OrderByDescending(grant => grant.CreatedAt).ThenByDescending(grant => grant.OrderItemId)
            .Take(query.Limit + 1)
            .Join(db.Products, grant => grant.ProductId, product => product.Id, (grant, product) => new { Grant = grant, product.Title, product.File, product.CoverImageKey, product.CoverImageUpdatedAt })
            .ToListAsync(cancellationToken);

        // The join does not keep the order, so sort again before paging.
        rows = [.. rows.OrderByDescending(row => row.Grant.CreatedAt).ThenByDescending(row => row.Grant.OrderItemId)];
        var now = timeProvider.GetUtcNow();
        var items = rows.ConvertAll(row => new LibraryItem(
            row.Grant.OrderItemId, row.Grant.ProductId, row.Title, row.File?.Name, row.Grant.CreatedAt, row.Grant.ExpiresAt,
            row.Grant.DownloadCount, row.Grant.MaxDownloads, row.Grant.GetUnavailableReason(now)?.ToString() ?? "Active",
            CoverUrls.For(images, row.CoverImageKey, row.CoverImageUpdatedAt)));
        return Paging.ToPage(items, query.Limit, item => new CreatedCursor(item.PurchasedAt, item.OrderItemId));
    }
}
