using System.Net;
using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

// Only the SHA-256 hash of the token is stored (D-17). Rotation and reuse detection arrive with T-021.
public sealed class RefreshToken : Entity
{
    public const int TokenHashLength = 64;
    public const int UserAgentMaxLength = 300;

    private RefreshToken(
        Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, IPAddress? createdIp, string? userAgent)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedIp = createdIp;
        UserAgent = userAgent;
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public IPAddress? CreatedIp { get; private set; }

    public string? UserAgent { get; private set; }

    public static RefreshToken Issue(
        Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, IPAddress? createdIp, string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tokenHash.Length, TokenHashLength);

        var agent = userAgent is { Length: > UserAgentMaxLength } ? userAgent[..UserAgentMaxLength] : userAgent;
        return new RefreshToken(userId, familyId, tokenHash, now, now + lifetime, createdIp, agent);
    }
}
