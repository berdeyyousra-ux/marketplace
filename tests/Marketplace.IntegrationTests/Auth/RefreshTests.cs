using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// EP-04, EP-05, US-04, US-05 and FM-15.
public sealed class RefreshTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US04_ValidRefresh_RotatesWithinTheSameFamily()
    {
        using var client = AsAnonymous();
        var first = await SignInAsync(client, "rotate@example.test");

        var response = await RefreshAsync(client, first);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = body.GetProperty("refreshToken").GetString()!;
        second.ShouldNotBe(first);
        var rows = await WithDbAsync(db => db.RefreshTokens.AsNoTracking().ToListAsync(CancellationToken));
        var old = rows.Single(row => row.TokenHash == SecureTokens.Hash(first));
        var successor = rows.Single(row => row.TokenHash == SecureTokens.Hash(second));
        old.UsedAt.ShouldBe(Time.GetUtcNow());
        old.ReplacedById.ShouldBe(successor.Id);
        successor.FamilyId.ShouldBe(old.FamilyId);
    }

    [Fact]
    public async Task FM15_ReusedRefreshToken_RevokesWholeFamily()
    {
        using var client = AsAnonymous();
        var first = await SignInAsync(client, "reuse-refresh@example.test");
        var second = (await (await RefreshAsync(client, first)).Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .GetProperty("refreshToken").GetString()!;

        var reuse = await RefreshAsync(client, first);
        var successorAfterReuse = await RefreshAsync(client, second);

        await ShouldFailWithAsync(reuse, "token_reuse_detected");
        await ShouldFailWithAsync(successorAfterReuse, "invalid_refresh_token");
        (await WithDbAsync(db => db.RefreshTokens.AllAsync(row => row.RevokedAt != null, CancellationToken))).ShouldBeTrue();
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "auth.token_reuse_detected", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US04_ExpiredRefreshToken_Returns401()
    {
        using var client = AsAnonymous();
        var token = await SignInAsync(client, "expired-refresh@example.test");

        Time.Advance(TimeSpan.FromDays(14) + TimeSpan.FromSeconds(1));

        await ShouldFailWithAsync(await RefreshAsync(client, token), "invalid_refresh_token");
    }

    [Fact]
    public async Task US04_UnknownRefreshToken_Returns401()
    {
        using var client = AsAnonymous();

        await ShouldFailWithAsync(await RefreshAsync(client, "not-a-real-token"), "invalid_refresh_token");
    }

    [Fact]
    public async Task US04_SameTokenConcurrently_OneWinsAndFamilyIsRevoked()
    {
        using var client = AsAnonymous();
        var token = await SignInAsync(client, "race-refresh@example.test");

        var responses = await Task.WhenAll(RefreshAsync(client, token), RefreshAsync(client, token));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Unauthorized]);
        (await WithDbAsync(db => db.RefreshTokens.AllAsync(row => row.RevokedAt != null, CancellationToken))).ShouldBeTrue();
    }

    [Fact]
    public async Task US05_Logout_RevokesFamilyAndAlwaysReturns204()
    {
        using var client = AsAnonymous();
        var token = await SignInAsync(client, "logout@example.test");

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = token }, CancellationToken);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = "not-a-real-token" }, CancellationToken);
        var refreshAfterLogout = await RefreshAsync(client, token);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldFailWithAsync(refreshAfterLogout, "invalid_refresh_token");
    }

    private async Task<string> SignInAsync(HttpClient client, string email)
    {
        await CreateVerifiedUserAsync(email);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = DefaultPassword }, CancellationToken);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await login.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("refreshToken").GetString()!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, TestContext.Current.CancellationToken);

    private static async Task ShouldFailWithAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }
}
