using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;

namespace Marketplace.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => AuthClaims.ReadUserId(accessor.HttpContext?.User);

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyList<string> Roles => AuthClaims.ReadRoles(accessor.HttpContext?.User);

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
}
