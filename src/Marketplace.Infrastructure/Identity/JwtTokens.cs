using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Infrastructure.Identity;

// Section 16 "Jwt" configuration.
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "marketplace-api";

    [Required]
    public string Audience { get; set; } = "marketplace";

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    public IReadOnlyList<string> PreviousSigningKeys { get; set; } = [];

    public static bool IsValidKey(string? base64) => TryDecode(base64, out var bytes) && bytes.Length >= MinimumKeyBytes;

    internal static bool TryDecode(string? base64, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class JwtTokens
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    // Section 11.1: HS256, issuer and audience checked, 30-second skew, current and previous keys accepted for rotation.
    // The lifetime check uses the injected TimeProvider (R-07), not the system clock.
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = [CreateKey(options.SigningKey), .. options.PreviousSigningKeys.Select(CreateKey)],
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            NameClaimType = AuthClaims.Name,
            RoleClaimType = AuthClaims.Role,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore.Value <= now + ClockSkew)
                    && expires is not null && expires.Value > now - ClockSkew;
            },
        };
    }

    // The key ID is derived from the key, so a token names the key that signed it.
    internal static SymmetricSecurityKey CreateKey(string base64)
    {
        if (!JwtOptions.TryDecode(base64, out var bytes) || bytes.Length < JwtOptions.MinimumKeyBytes)
        {
            throw new InvalidOperationException($"JWT signing keys must be base64 and at least {JwtOptions.MinimumKeyBytes} bytes.");
        }

        return new SymmetricSecurityKey(bytes) { KeyId = Convert.ToHexString(SHA256.HashData(bytes))[..16] };
    }
}

public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(AccessTokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [AuthClaims.Subject] = subject.UserId.ToString(),
            [AuthClaims.Email] = subject.Email,
            [AuthClaims.Name] = subject.DisplayName,
            [AuthClaims.TokenId] = Guid.CreateVersion7().ToString(),
        };
        if (subject.Roles.Count > 0)
        {
            claims[AuthClaims.Role] = subject.Roles.ToArray();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(JwtTokens.CreateKey(jwt.SigningKey), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expiresAt);
    }
}
