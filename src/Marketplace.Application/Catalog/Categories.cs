using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Catalog;

// EP-20.
public sealed record CategoriesQuery;

public sealed record CategoryResponse(int Id, string Slug, string Name);

public sealed class CategoriesQueryHandler(IAppDbContext db) : IQueryHandler<CategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CategoryResponse>>> HandleAsync(CategoriesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<CategoryResponse> categories = await db.Categories.AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .Select(category => new CategoryResponse(category.Id, category.Slug, category.Name))
            .ToListAsync(cancellationToken);
        return Result.Success(categories);
    }
}
