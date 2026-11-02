using System.Security.Claims;

namespace Marketplace.Application.Common;

public static class Roles
{
    public const string Seller = "Seller";
    public const string Admin = "Admin";
}

// Section 8.4 claim names. Inbound claim mapping is off, so these arrive exactly as issued.
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string TokenId = "jti";
    public const string Role = "role";

    public static Guid? ReadUserId(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true && Guid.TryParse(principal.FindFirst(Subject)?.Value, out var id) && id != Guid.Empty
            ? id
            : null;

    public static IReadOnlyList<string> ReadRoles(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
            ? principal.FindAll(Role).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToList()
            : [];
}
