using System.Security.Claims;
using Marketplace.Application.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Application.Common;

public sealed class AuthClaimsTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    [Fact]
    public void ReadUserId_AuthenticatedWithSubject_ReturnsId()
    {
        var principal = Principal(new Claim(AuthClaims.Subject, _userId.ToString()));

        AuthClaims.ReadUserId(principal).ShouldBe(_userId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ReadUserId_InvalidSubject_ReturnsNull(string subject)
    {
        AuthClaims.ReadUserId(Principal(new Claim(AuthClaims.Subject, subject))).ShouldBeNull();
    }

    [Fact]
    public void ReadUserId_Anonymous_ReturnsNull()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaims.Subject, _userId.ToString())]));

        AuthClaims.ReadUserId(anonymous).ShouldBeNull();
        AuthClaims.ReadUserId(null).ShouldBeNull();
    }

    [Fact]
    public void ReadRoles_RoleClaims_ReturnsDistinctRoles()
    {
        var principal = Principal(
            new Claim(AuthClaims.Role, Roles.Seller), new Claim(AuthClaims.Role, Roles.Admin), new Claim(AuthClaims.Role, Roles.Seller));

        AuthClaims.ReadRoles(principal).ShouldBe([Roles.Seller, Roles.Admin]);
    }

    [Fact]
    public void ReadRoles_Anonymous_ReturnsEmpty()
    {
        AuthClaims.ReadRoles(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeEmpty();
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));
}
