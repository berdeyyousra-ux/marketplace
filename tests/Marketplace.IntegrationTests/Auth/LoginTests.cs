using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// EP-03 and US-03.
public sealed class LoginTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US03_CorrectCredentials_ReturnsTokenPairAndStoresHashedRefreshToken()
    {
        var userId = await CreateVerifiedUserAsync("login@example.test");
        using var client = AsAnonymous();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LoginTests/1.0");

        var response = await LoginAsync(client, "login@example.test", DefaultPassword);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        body.GetProperty("expiresInSeconds").GetInt32().ShouldBe(900);
        body.GetProperty("accessToken").GetString()!.Split('.').Length.ShouldBe(3);
        var refresh = body.GetProperty("refreshToken").GetString()!;
        var stored = await WithDbAsync(db => db.RefreshTokens.SingleAsync(CancellationToken));
        stored.UserId.ShouldBe(userId);
        stored.TokenHash.ShouldBe(SecureTokens.Hash(refresh));
        stored.ExpiresAt.ShouldBe(Time.GetUtcNow().AddDays(14));
        stored.UserAgent.ShouldBe("LoginTests/1.0");
        (await AuditActionsAsync()).ShouldContain("auth.login_succeeded");
    }

    [Fact]
    public async Task US03_WrongPasswordAndUnknownEmail_ReturnIdenticalErrors()
    {
        await CreateVerifiedUserAsync("known@example.test");
        using var client = AsAnonymous();

        var wrongPassword = await LoginAsync(client, "known@example.test", "not the right passphrase");
        var unknownEmail = await LoginAsync(client, "nobody@example.test", "not the right passphrase");
        var first = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var second = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        first.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        second.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        first.GetProperty("detail").GetString().ShouldBe(second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task US03_UnconfirmedEmail_Returns403OnlyWithCorrectPassword()
    {
        using var client = AsAnonymous();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "pending@example.test", password = DefaultPassword, displayName = "Pending" }, CancellationToken);

        var correct = await LoginAsync(client, "pending@example.test", DefaultPassword);
        var wrong = await LoginAsync(client, "pending@example.test", "not the right passphrase");

        correct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await correct.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("email_not_verified");
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task US03_Login_LockedAfterFiveFailures()
    {
        await CreateVerifiedUserAsync("lock@example.test");
        using var client = AsAnonymous();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            (await LoginAsync(client, "lock@example.test", "not the right passphrase")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var whileLocked = await LoginAsync(client, "lock@example.test", DefaultPassword);
        Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var afterLockout = await LoginAsync(client, "lock@example.test", DefaultPassword);

        whileLocked.StatusCode.ShouldBe((HttpStatusCode)423);
        (await whileLocked.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("locked_out");
        afterLockout.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditActionsAsync()).Count(action => action == "auth.locked_out").ShouldBe(1);
    }

    [Fact]
    public async Task US03_SuccessfulLogin_ResetsFailureCount()
    {
        await CreateVerifiedUserAsync("reset-count@example.test");
        using var client = AsAnonymous();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await LoginAsync(client, "reset-count@example.test", "not the right passphrase");
        }

        await LoginAsync(client, "reset-count@example.test", DefaultPassword);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await LoginAsync(client, "reset-count@example.test", "not the right passphrase");
        }

        (await LoginAsync(client, "reset-count@example.test", DefaultPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task US03_FailedLoginAudit_HoldsOnlyTheReasonClass()
    {
        await CreateVerifiedUserAsync("audit@example.test");
        using var client = AsAnonymous();

        await LoginAsync(client, "audit@example.test", "not the right passphrase");
        await LoginAsync(client, "ghost@example.test", "not the right passphrase");

        var details = await WithDbAsync(db => db.AuditLog.Where(entry => entry.Action == "auth.login_failed").Select(entry => entry.Details!).ToListAsync(CancellationToken));
        details.Count.ShouldBe(2);
        details.ShouldAllBe(detail => !detail.Contains("example.test") && !detail.Contains("passphrase"));
        details.ShouldContain(detail => detail.Contains("unknown_account"));
        details.ShouldContain(detail => detail.Contains("wrong_password"));
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    private Task<List<string>> AuditActionsAsync() =>
        WithDbAsync(db => db.AuditLog.Select(entry => entry.Action).ToListAsync(CancellationToken));
}
