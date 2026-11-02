using Marketplace.Application.Catalog;
using Marketplace.Domain.Catalog;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Marketplace.Infrastructure.Catalog;

// Section 13.3: websearch_to_tsquery('english', q) against the generated search_vector, ts_rank_cd for relevance,
// and keyset order on (published_at, id) or (price, id) for the other sorts.
public sealed class ProductSearch(AppDbContext db) : IProductSearch
{
    private const string _textConfig = "english";

    public async Task<List<ProductSearchRow>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var products = db.Products.AsNoTracking().Where(product => product.Status == ProductStatus.Published);

        if (criteria.CategorySlug is not null)
        {
            products = products.Where(product => db.Categories.Any(category => category.Id == product.CategoryId && category.Slug == criteria.CategorySlug));
        }

        if (criteria.MinPrice is not null)
        {
            products = products.Where(product => product.Price.Amount >= criteria.MinPrice);
        }

        if (criteria.MaxPrice is not null)
        {
            products = products.Where(product => product.Price.Amount <= criteria.MaxPrice);
        }

        if (criteria.Q is not null)
        {
            products = products.Where(product =>
                EF.Property<NpgsqlTsVector>(product, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery(_textConfig, criteria.Q)));
        }

        var after = criteria.After;
        var ordered = criteria.Sort switch
        {
            ProductSort.Relevance => products
                .OrderByDescending(product => EF.Property<NpgsqlTsVector>(product, "SearchVector")
                    .RankCoverDensity(EF.Functions.WebSearchToTsQuery(_textConfig, criteria.Q!)))
                .ThenBy(product => product.Id),
            ProductSort.PriceAsc => (after is null ? products : products.Where(product =>
                    product.Price.Amount > after.Price || (product.Price.Amount == after.Price && product.Id.CompareTo(after.Id) > 0)))
                .OrderBy(product => product.Price.Amount).ThenBy(product => product.Id),
            ProductSort.PriceDesc => (after is null ? products : products.Where(product =>
                    product.Price.Amount < after.Price || (product.Price.Amount == after.Price && product.Id.CompareTo(after.Id) < 0)))
                .OrderByDescending(product => product.Price.Amount).ThenByDescending(product => product.Id),
            _ => (after is null ? products : products.Where(product =>
                    product.PublishedAt < after.PublishedAt || (product.PublishedAt == after.PublishedAt && product.Id.CompareTo(after.Id) < 0)))
                .OrderByDescending(product => product.PublishedAt).ThenByDescending(product => product.Id),
        };

        return await ordered
            .Take(criteria.Take)
            .Select(product => new ProductSearchRow(
                product.Id, product.Title, product.Price.Amount, product.Price.Currency, product.CategoryId, product.SellerId, product.PublishedAt!.Value,
                product.CoverImageKey, product.CoverImageUpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
