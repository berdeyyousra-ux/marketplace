using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public enum OneTimeTokenPurpose
{
    EmailVerification,
    PasswordReset,
}

// Single-use token for email verification and password reset; only its SHA-256 hash is stored (D-17).
public sealed class OneTimeToken : Entity
{
    private OneTimeToken(Guid userId, OneTimeTokenPurpose purpose, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public OneTimeTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static OneTimeToken Issue(Guid userId, OneTimeTokenPurpose purpose, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tokenHash.Length, RefreshToken.TokenHashLength);

        return new OneTimeToken(userId, purpose, tokenHash, now, now + lifetime);
    }
}
