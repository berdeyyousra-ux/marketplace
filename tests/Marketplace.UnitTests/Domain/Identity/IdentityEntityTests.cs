using Marketplace.Domain.Identity;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Identity;

public sealed class IdentityEntityTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly string _hash = new('a', 64);

    [Fact]
    public void SellerProfileCreate_ValidDetails_Succeeds()
    {
        var result = SellerProfile.Create(Guid.CreateVersion7(), "Studio Lumen", " ", "ES", _now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Bio.ShouldBeNull();
        result.Value.Country.ShouldBe("ES");
    }

    [Theory]
    [InlineData("S", null, "ES")]
    [InlineData("Studio Lumen", null, "es")]
    [InlineData("Studio Lumen", null, "ESP")]
    public void SellerProfileCreate_InvalidDetails_ReturnsValidationFailed(string displayName, string? bio, string country)
    {
        var result = SellerProfile.Create(Guid.CreateVersion7(), displayName, bio, country, _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void SellerProfileCreate_TooLongBio_ReturnsValidationFailed()
    {
        var result = SellerProfile.Create(Guid.CreateVersion7(), "Studio Lumen", new string('b', 1001), "ES", _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void RefreshTokenIssue_Lifetime_SetsExpiryAndShortensUserAgent()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), _hash, _now, TimeSpan.FromDays(14), null, new string('u', 400));

        token.ExpiresAt.ShouldBe(_now.AddDays(14));
        token.UserAgent!.Length.ShouldBe(300);
        token.UsedAt.ShouldBeNull();
    }

    [Fact]
    public void RefreshTokenIssue_WrongHashLength_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "abc", _now, TimeSpan.FromDays(14), null, null));
    }

    [Fact]
    public void OneTimeTokenIssue_Lifetime_SetsPurposeAndExpiry()
    {
        var token = OneTimeToken.Issue(Guid.CreateVersion7(), OneTimeTokenPurpose.PasswordReset, _hash, _now, TimeSpan.FromMinutes(30));

        token.Purpose.ShouldBe(OneTimeTokenPurpose.PasswordReset);
        token.ExpiresAt.ShouldBe(_now.AddMinutes(30));
    }
}
