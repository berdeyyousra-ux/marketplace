using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Catalog;

// EP-28 and US-15.
public sealed record SellerProductsQuery(string? Status = null, int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record SellerProductSummary(
    Guid Id, string Title, string Status, MoneyDto Price, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, int SalesCount,
    Uri? CoverImageUrl);

public sealed record CreatedCursor(DateTimeOffset CreatedAt, Guid Id);

public sealed class SellerProductsQueryValidator : AbstractValidator<SellerProductsQuery>
{
    public SellerProductsQueryValidator()
    {
        RuleFor(query => query.Limit).ValidLimit();
        RuleFor(query => query.Status).Must(status => status is null || Enum.TryParse<ProductStatus>(status, ignoreCase: false, out _))
            .WithMessage("Status must be Draft, Published, Unpublished or Removed.");
    }
}

// Newest first, keyset on (created_at, id). Sales are paid, not refunded order items.
public sealed class SellerProductsQueryHandler(IAppDbContext db, ProductOwnership ownership, IPublicImageStorage images)
    : IQueryHandler<SellerProductsQuery, Page<SellerProductSummary>>
{
    public async Task<Result<Page<SellerProductSummary>>> HandleAsync(SellerProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<CreatedCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("Listing seller products requires a seller profile; the Seller policy guarantees one.");

        var products = db.Products.AsNoTracking().Where(product => product.SellerId == sellerId);
        if (query.Status is not null)
        {
            var status = Enum.Parse<ProductStatus>(query.Status);
            products = products.Where(product => product.Status == status);
        }

        if (cursor is not null)
        {
            products = products.Where(product => product.CreatedAt < cursor.CreatedAt
                || (product.CreatedAt == cursor.CreatedAt && product.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await products
            .OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id)
            .Take(query.Limit + 1)
            .Select(product => new
            {
                product.Id,
                product.Title,
                product.Status,
                product.Price,
                product.CreatedAt,
                product.UpdatedAt,
                product.PublishedAt,
                product.CoverImageKey,
                product.CoverImageUpdatedAt,
                SalesCount = db.OrderItems.Count(item => item.ProductId == product.Id
                    && item.Status == OrderItemStatus.Active
                    && db.Orders.Any(order => order.Id == item.OrderId && order.PaidAt != null)),
            })
            .ToListAsync(cancellationToken);

        var summaries = rows.ConvertAll(row => new SellerProductSummary(
            row.Id, row.Title, row.Status.ToString(), MoneyDto.From(row.Price), row.CreatedAt, row.UpdatedAt, row.PublishedAt, row.SalesCount,
            CoverUrls.For(images, row.CoverImageKey, row.CoverImageUpdatedAt)));
        return Paging.ToPage(summaries, query.Limit, summary => new CreatedCursor(summary.CreatedAt, summary.Id));
    }
}
