using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// EP-10, EP-11, EP-20 and US-10.
public sealed class SellerAndCategoryTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US10_BecomeSeller_CreatesProfileGrantsRoleAndAsksForRefresh()
    {
        await CreateVerifiedUserAsync("seller@example.test");
        var tokens = await SignInAsync("seller@example.test");
        using var client = WithToken(tokens.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/sellers", new { displayName = "Studio Lumen", bio = "Templates.", country = "ES" }, CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe("/api/v1/sellers/me");
        body.GetProperty("displayName").GetString().ShouldBe("Studio Lumen");
        body.GetProperty("requiresTokenRefresh").GetBoolean().ShouldBeTrue();
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "seller.created", CancellationToken))).ShouldBe(1);

        using var refreshed = WithToken((await RefreshTokensAsync(tokens.RefreshToken)).AccessToken);
        var me = await refreshed.GetFromJsonAsync<JsonElement>("/api/v1/me", CancellationToken);
        me.GetProperty("roles")[0].GetString().ShouldBe("Seller");
        me.GetProperty("sellerProfile").GetProperty("country").GetString().ShouldBe("ES");
    }

    [Fact]
    public async Task US10_BecomeSellerTwice_Returns409AlreadySeller()
    {
        using var client = await AsSellerAsync("twice-seller@example.test");

        var again = await client.PostAsJsonAsync("/api/v1/sellers", new { displayName = "Second", bio = (string?)null, country = "ES" }, CancellationToken);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("already_seller");
    }

    [Fact]
    public async Task FM13_BecomeSellerConcurrently_OneCreatedOneAlreadySeller()
    {
        using var client = await AsBuyerAsync("race-seller@example.test");

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/sellers", new { displayName = "First", bio = (string?)null, country = "ES" }, CancellationToken),
            client.PostAsJsonAsync("/api/v1/sellers", new { displayName = "Second", bio = (string?)null, country = "ES" }, CancellationToken));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await WithDbAsync(db => db.SellerProfiles.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Theory]
    [InlineData("S", "ES")]
    [InlineData("Studio", "es")]
    [InlineData("Studio", "ESP")]
    public async Task US10_InvalidProfile_Returns422(string displayName, string country)
    {
        using var client = await AsBuyerAsync($"invalid-seller-{displayName}{country}@example.test");

        var response = await client.PostAsJsonAsync("/api/v1/sellers", new { displayName, bio = (string?)null, country }, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task EP11_SellerProfile_RequiresSellerRole()
    {
        using var buyer = await AsBuyerAsync("not-a-seller@example.test");
        using var seller = await AsSellerAsync("is-a-seller@example.test", "Studio Fern");

        var forbidden = await buyer.GetAsync("/api/v1/sellers/me", CancellationToken);
        var allowed = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me", CancellationToken);

        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("forbidden");
        allowed.GetProperty("displayName").GetString().ShouldBe("Studio Fern");
        allowed.GetProperty("requiresTokenRefresh").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task EP20_Categories_ListedInOrderAndCacheable()
    {
        using var client = AsAnonymous();

        var response = await client.GetAsync("/api/v1/categories", CancellationToken);
        var categories = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromMinutes(5));
        response.Headers.CacheControl.Public.ShouldBeTrue();
        categories.EnumerateArray().Select(category => category.GetProperty("slug").GetString())
            .ShouldBe(["ebooks", "templates", "graphics", "audio", "video", "software", "fonts", "other"]);
    }
}
