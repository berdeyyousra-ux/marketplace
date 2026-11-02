using System.Security.Cryptography;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// Token issue and validation rules from sections 8.4 and 11.1. These need no containers; they live here
// because the unit test project cannot reference Infrastructure (section 5.2).
public sealed class JwtTokenTests
{
    private static readonly AccessTokenSubject _subject =
        new(Guid.CreateVersion7(), "buyer@example.test", "Buyer", [Roles.Seller, Roles.Admin]);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly JwtOptions _options = new() { SigningKey = NewKey() };

    [Fact]
    public async Task Issue_ValidSubject_CarriesSection84Claims()
    {
        var token = Issuer().Issue(_subject);

        var result = await ValidateAsync(token.Token);

        result.IsValid.ShouldBeTrue();
        result.Claims[AuthClaims.Subject].ShouldBe(_subject.UserId.ToString());
        result.Claims[AuthClaims.Email].ShouldBe(_subject.Email);
        result.Claims[AuthClaims.Name].ShouldBe(_subject.DisplayName);
        result.Claims.ShouldContainKey(AuthClaims.TokenId);
        result.ClaimsIdentity.FindAll(AuthClaims.Role).Select(claim => claim.Value).ShouldBe([Roles.Seller, Roles.Admin]);
        token.ExpiresAt.ShouldBe(_time.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public async Task Issue_NoRoles_OmitsRoleClaim()
    {
        var token = Issuer().Issue(_subject with { Roles = [] });

        var result = await ValidateAsync(token.Token);

        result.Claims.ShouldNotContainKey(AuthClaims.Role);
    }

    [Fact]
    public async Task Validate_WithinClockSkewAfterExpiry_IsValid()
    {
        var token = Issuer().Issue(_subject);

        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(29));

        (await ValidateAsync(token.Token)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_BeyondClockSkewAfterExpiry_IsRejected()
    {
        var token = Issuer().Issue(_subject);

        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(31));

        (await ValidateAsync(token.Token)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_SignedWithPreviousKey_IsAcceptedDuringRotation()
    {
        var oldKey = _options.SigningKey;
        var token = Issuer().Issue(_subject);
        var rotated = new JwtOptions { SigningKey = NewKey(), PreviousSigningKeys = [oldKey] };

        (await ValidateAsync(token.Token, rotated)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_SignedWithUnknownKey_IsRejected()
    {
        var token = Issuer().Issue(_subject);

        (await ValidateAsync(token.Token, new JwtOptions { SigningKey = NewKey() })).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("other-issuer", "marketplace")]
    [InlineData("marketplace-api", "other-audience")]
    public async Task Validate_WrongIssuerOrAudience_IsRejected(string issuer, string audience)
    {
        var token = Issuer(new JwtOptions { SigningKey = _options.SigningKey, Issuer = issuer, Audience = audience }).Issue(_subject);

        (await ValidateAsync(token.Token)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_TamperedPayload_IsRejected()
    {
        var parts = Issuer().Issue(_subject).Token.Split('.');
        var forged = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("Buyer", "Admin", StringComparison.Ordinal));

        (await ValidateAsync($"{parts[0]}.{forged}.{parts[2]}")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_UnsignedToken_IsRejected()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = _time.GetUtcNow().AddMinutes(15).UtcDateTime,
            Claims = new Dictionary<string, object> { [AuthClaims.Subject] = _subject.UserId.ToString() },
        });

        (await ValidateAsync(unsigned)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void IsValidKey_EmptyMalformedOrShort_IsFalse(string key)
    {
        JwtOptions.IsValidKey(key).ShouldBeFalse();
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private JwtAccessTokenIssuer Issuer(JwtOptions? options = null) => new(Options.Create(options ?? _options), _time);

    private Task<TokenValidationResult> ValidateAsync(string token, JwtOptions? options = null) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokens.CreateValidationParameters(options ?? _options, _time));
}
