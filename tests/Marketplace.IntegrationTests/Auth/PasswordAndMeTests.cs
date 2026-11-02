using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Infrastructure.Outbox;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// EP-06 to EP-09, US-06 to US-08 and FM-24.
public sealed class PasswordAndMeTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private const string _newPassword = "a brand new passphrase";

    private FakeEmailSender Email => Factory.Services.GetRequiredService<FakeEmailSender>();

    [Fact]
    public async Task US06_ForgotPassword_EmailsOnlyExistingAccountsAndAlwaysReturns202()
    {
        await CreateVerifiedUserAsync("forgot@example.test");
        using var client = AsAnonymous();

        var existing = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "forgot@example.test" }, CancellationToken);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.test" }, CancellationToken);
        await ProcessOutboxAsync();

        existing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Email.LatestToken("forgot@example.test", "reset-password").ShouldNotBeNullOrEmpty();
        Email.To("nobody@example.test").ShouldBeEmpty();
    }

    [Fact]
    public async Task US07_ResetPassword_SetsPasswordAndRevokesAllSessions()
    {
        using var client = AsAnonymous();
        var refresh = await SignInAsync(client, "reset@example.test");
        var token = await RequestResetTokenAsync(client, "reset@example.test");

        var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = _newPassword }, CancellationToken);

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(client, "reset@example.test", DefaultPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, "reset@example.test", _newPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh }, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "auth.password_reset", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task FM24_ResetTokenReusedOrExpired_Returns400InvalidToken()
    {
        using var client = AsAnonymous();
        await CreateVerifiedUserAsync("reset-reuse@example.test");
        var used = await RequestResetTokenAsync(client, "reset-reuse@example.test");
        await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = used, newPassword = _newPassword }, CancellationToken);
        var expiring = await RequestResetTokenAsync(client, "reset-reuse@example.test");

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = used, newPassword = "yet another passphrase" }, CancellationToken);
        Time.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        var expired = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = expiring, newPassword = "yet another passphrase" }, CancellationToken);

        await ShouldHaveCodeAsync(reuse, HttpStatusCode.BadRequest, "invalid_token");
        await ShouldHaveCodeAsync(expired, HttpStatusCode.BadRequest, "invalid_token");
    }

    [Fact]
    public async Task US07_CommonNewPassword_Returns422AndKeepsTokenUnused()
    {
        using var client = AsAnonymous();
        await CreateVerifiedUserAsync("reset-common@example.test");
        var token = await RequestResetTokenAsync(client, "reset-common@example.test");

        var weak = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "1234567890" }, CancellationToken);
        var strong = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = _newPassword }, CancellationToken);

        weak.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        strong.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task US08_ChangePassword_RequiresCurrentPasswordAndRevokesSessions()
    {
        using var client = AsAnonymous();
        var (access, refresh) = await SignInWithAccessAsync(client, "change@example.test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "not my password", newPassword = _newPassword }, CancellationToken);
        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = DefaultPassword, newPassword = _newPassword }, CancellationToken);

        await ShouldHaveCodeAsync(wrong, HttpStatusCode.UnprocessableEntity, "validation_failed");
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(client, "change@example.test", _newPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh }, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task US08_ChangePassword_WithoutToken_Returns401()
    {
        using var client = AsAnonymous();

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "x", newPassword = _newPassword }, CancellationToken);

        await ShouldHaveCodeAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task EP09_Me_ReturnsSignedInAccount()
    {
        using var client = AsAnonymous();
        var (access, _) = await SignInWithAccessAsync(client, "me@example.test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.GetAsync("/api/v1/me", CancellationToken);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        me.GetProperty("email").GetString().ShouldBe("me@example.test");
        me.GetProperty("displayName").GetString().ShouldBe("Test User");
        me.GetProperty("roles").GetArrayLength().ShouldBe(0);
        me.GetProperty("sellerProfile").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task EP09_Me_WithoutOrWithExpiredToken_Returns401ProblemDetails()
    {
        using var client = AsAnonymous();
        var (access, _) = await SignInWithAccessAsync(client, "me-expired@example.test");

        var anonymous = await client.GetAsync("/api/v1/me", CancellationToken);
        Time.Advance(TimeSpan.FromMinutes(16));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var expired = await client.GetAsync("/api/v1/me", CancellationToken);

        await ShouldHaveCodeAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");
        expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<int> ProcessOutboxAsync() => Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

    private async Task<string> RequestResetTokenAsync(HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }, CancellationToken);
        await ProcessOutboxAsync();
        return Email.LatestToken(email, "reset-password");
    }

    private async Task<string> SignInAsync(HttpClient client, string email) => (await SignInWithAccessAsync(client, email)).Refresh;

    private async Task<(string Access, string Refresh)> SignInWithAccessAsync(HttpClient client, string email)
    {
        await CreateVerifiedUserAsync(email);
        var body = await (await LoginAsync(client, email, DefaultPassword)).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }
}
