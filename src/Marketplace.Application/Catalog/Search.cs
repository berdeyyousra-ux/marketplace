using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Catalog;

// EP-21, US-20 and section 13.3.
public enum ProductSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    Relevance,
}

public sealed record SearchProductsQuery(
    string? Q = null,
    string? CategorySlug = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSort Sort = ProductSort.Newest,
    int Limit = Paging.DefaultLimit,
    string? Cursor = null);

public sealed record ProductSummary(Guid Id, string Title, MoneyDto Price, int CategoryId, Guid SellerId, DateTimeOffset PublishedAt);

// One cursor shape for every keyset sort; the sort is stored so a cursor cannot be replayed under another sort.
public sealed record SearchCursor(ProductSort Sort, DateTimeOffset? PublishedAt, decimal? Price, Guid Id);

public sealed record ProductSearchCriteria(
    string? Q, string? CategorySlug, decimal? MinPrice, decimal? MaxPrice, ProductSort Sort, SearchCursor? After, int Take);

public sealed record ProductSearchRow(Guid Id, string Title, decimal Price, string Currency, int CategoryId, Guid SellerId, DateTimeOffset PublishedAt);

// Full-text search uses PostgreSQL functions, which Application must not reference (section 4.3), so the query
// itself lives in Infrastructure.
public interface IProductSearch
{
    Task<List<ProductSearchRow>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken);
}

public sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public const int MaxQueryLength = 200;

    public SearchProductsQueryValidator()
    {
        RuleFor(query => query.Limit).ValidLimit();
        RuleFor(query => query.Q).MaximumLength(MaxQueryLength);
        RuleFor(query => query.CategorySlug).MaximumLength(60);
        RuleFor(query => query.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(query => query.MaxPrice).GreaterThanOrEqualTo(0);
        RuleFor(query => query).Must(query => query.MinPrice is null || query.MaxPrice is null || query.MinPrice <= query.MaxPrice)
            .WithName("minPrice").WithMessage("minPrice must not be greater than maxPrice.");
        RuleFor(query => query.Sort).Must((query, sort) => sort != ProductSort.Relevance || !string.IsNullOrWhiteSpace(query.Q))
            .WithMessage("Sorting by relevance needs a search term (q).");
        RuleFor(query => query.Sort).IsInEnum();
    }
}

public sealed class SearchProductsQueryHandler(IProductSearch search) : IQueryHandler<SearchProductsQuery, Page<ProductSummary>>
{
    // Section 13.3: relevance is not a safe keyset, so it returns at most 100 rows and no cursor.
    public const int RelevanceMaxRows = 100;

    public async Task<Result<Page<ProductSummary>>> HandleAsync(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<SearchCursor>(query.Cursor, out var cursor)
            || (cursor is not null && (query.Sort == ProductSort.Relevance || cursor.Sort != query.Sort)))
        {
            return Paging.InvalidCursor;
        }

        var q = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q;
        if (query.Sort == ProductSort.Relevance)
        {
            var ranked = await search.SearchAsync(
                new ProductSearchCriteria(q, query.CategorySlug, query.MinPrice, query.MaxPrice, query.Sort, null, RelevanceMaxRows), cancellationToken);
            return new Page<ProductSummary>(ranked.ConvertAll(ToSummary), null);
        }

        var rows = await search.SearchAsync(
            new ProductSearchCriteria(q, query.CategorySlug, query.MinPrice, query.MaxPrice, query.Sort, cursor, query.Limit + 1), cancellationToken);
        return Paging.ToPage(rows.ConvertAll(ToSummary), query.Limit, summary => new SearchCursor(
            query.Sort,
            query.Sort == ProductSort.Newest ? summary.PublishedAt : null,
            query.Sort == ProductSort.Newest ? null : decimal.Parse(summary.Price.Amount, System.Globalization.CultureInfo.InvariantCulture),
            summary.Id));
    }

    private static ProductSummary ToSummary(ProductSearchRow row) =>
        new(row.Id, row.Title, MoneyDto.From(new Money(row.Price, row.Currency)), row.CategoryId, row.SellerId, row.PublishedAt);
}
