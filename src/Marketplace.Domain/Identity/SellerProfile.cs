using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class SellerProfile : AggregateRoot
{
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 80;
    public const int BioMaxLength = 1000;

    private SellerProfile(Guid userId, string displayName, string? bio, string country, DateTimeOffset createdAt)
    {
        UserId = userId;
        DisplayName = displayName;
        Bio = bio;
        Country = country;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; }

    public string? Bio { get; private set; }

    public string Country { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<SellerProfile> Create(Guid userId, string displayName, string? bio, string country, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            return DomainErrors.Validation("User is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length is < DisplayNameMinLength or > DisplayNameMaxLength)
        {
            return DomainErrors.Validation($"Display name must be {DisplayNameMinLength} to {DisplayNameMaxLength} characters.");
        }

        if (bio is { Length: > BioMaxLength })
        {
            return DomainErrors.Validation($"Bio must be at most {BioMaxLength} characters.");
        }

        if (country is not { Length: 2 } || !country.All(char.IsAsciiLetterUpper))
        {
            return DomainErrors.Validation("Country must be a two-letter uppercase code.");
        }

        return new SellerProfile(userId, displayName, string.IsNullOrWhiteSpace(bio) ? null : bio, country, now);
    }
}
