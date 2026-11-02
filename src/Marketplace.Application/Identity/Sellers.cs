using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Identity;

// EP-10, EP-11 and US-10.
public sealed record BecomeSellerCommand(string DisplayName, string? Bio, string Country);

public sealed record SellerQuery;

public sealed record SellerProfileResponse(Guid Id, string DisplayName, string? Bio, string Country, DateTimeOffset CreatedAt, bool RequiresTokenRefresh);

public sealed class BecomeSellerCommandValidator : AbstractValidator<BecomeSellerCommand>
{
    public BecomeSellerCommandValidator()
    {
        RuleFor(command => command.DisplayName).NotEmpty().Length(SellerProfile.DisplayNameMinLength, SellerProfile.DisplayNameMaxLength);
        RuleFor(command => command.Bio).MaximumLength(SellerProfile.BioMaxLength);
        RuleFor(command => command.Country).NotEmpty().Matches("^[A-Z]{2}$").WithMessage("Country must be a two-letter uppercase code, such as ES.");
    }
}

public static class SellerErrors
{
    public static readonly Error AlreadySeller = new("already_seller", "You already have a seller profile.", ErrorType.Conflict);
}

// Creates the profile and grants the Seller role in one transaction. Roles are read from the access token,
// so the response tells the client to refresh its tokens (section 8.4).
public sealed class BecomeSellerCommandHandler(
    IAppDbContext db, IIdentityService identity, ICurrentUser currentUser, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<BecomeSellerCommand, SellerProfileResponse>
{
    public async Task<Result<SellerProfileResponse>> HandleAsync(BecomeSellerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Becoming a seller requires an authenticated caller.");
        if (await db.SellerProfiles.AnyAsync(profile => profile.UserId == userId, cancellationToken))
        {
            return SellerErrors.AlreadySeller;
        }

        var created = SellerProfile.Create(userId, command.DisplayName, command.Bio, command.Country, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var profile = created.Value;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        db.SellerProfiles.Add(profile);
        audit.Record("seller.created", AuditActorType.User, userId, "SellerProfile", profile.Id);

        // A concurrent duplicate fails here on ux_seller_profiles_user_id and becomes 409 already_seller (FM-13).
        await db.SaveChangesAsync(cancellationToken);
        await identity.AddToRoleAsync(userId, Roles.Seller, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(profile, requiresTokenRefresh: true);
    }

    internal static SellerProfileResponse ToResponse(SellerProfile profile, bool requiresTokenRefresh) =>
        new(profile.Id, profile.DisplayName, profile.Bio, profile.Country, profile.CreatedAt, requiresTokenRefresh);
}

public sealed class SellerQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<SellerQuery, SellerProfileResponse>
{
    public async Task<Result<SellerProfileResponse>> HandleAsync(SellerQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Reading a seller profile requires an authenticated caller.");
        var profile = await db.SellerProfiles.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        return profile is null
            ? new Error("not_found", "No seller profile exists for this account.", ErrorType.NotFound)
            : BecomeSellerCommandHandler.ToResponse(profile, requiresTokenRefresh: false);
    }
}
