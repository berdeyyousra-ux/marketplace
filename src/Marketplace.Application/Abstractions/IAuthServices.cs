namespace Marketplace.Application.Abstractions;

// The signed-in caller, read from token claims. Handlers use this, never HttpContext (section 4.5).
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsInRole(string role);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record AccessTokenSubject(Guid UserId, string Email, string DisplayName, IReadOnlyList<string> Roles);

// Issues the 15-minute HS256 access token (D-16).
public interface IAccessTokenIssuer
{
    AccessToken Issue(AccessTokenSubject subject);
}
