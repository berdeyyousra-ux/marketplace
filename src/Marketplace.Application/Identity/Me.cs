using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Identity;

// EP-09.
public sealed record MeQuery;

public sealed record SellerProfileSummary(Guid Id, string DisplayName, string Country);

public sealed record MeResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, SellerProfileSummary? SellerProfile);

public sealed class MeQueryHandler(IIdentityService identity, ICurrentUser currentUser, IAppDbContext db) : IQueryHandler<MeQuery, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(MeQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("GET /me requires an authenticated caller.");
        var account = await identity.GetAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return new Error("not_found", "The account no longer exists.", ErrorType.NotFound);
        }

        var seller = await db.SellerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => new SellerProfileSummary(profile.Id, profile.DisplayName, profile.Country))
            .SingleOrDefaultAsync(cancellationToken);

        return new MeResponse(userId, account.Email, account.DisplayName, await identity.GetRolesAsync(userId, cancellationToken), seller);
    }
}
