using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Infrastructure.Outbox;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// EP-02, US-02 and FM-24, end to end: register, the outbox handler emails a token, the token confirms the email.
public sealed class VerifyEmailTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakeEmailSender Email => Factory.Services.GetRequiredService<FakeEmailSender>();

    [Fact]
    public async Task US02_ValidToken_ConfirmsEmail()
    {
        using var client = AsAnonymous();
        var token = await RegisterAndGetTokenAsync(client, "verify@example.test");

        var response = await VerifyAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.Users.Select(user => user.EmailConfirmed).SingleAsync(CancellationToken))).ShouldBeTrue();
    }

    [Fact]
    public async Task FM24_ReusedToken_Returns400InvalidToken()
    {
        using var client = AsAnonymous();
        var token = await RegisterAndGetTokenAsync(client, "reuse@example.test");
        await VerifyAsync(client, token);

        var response = await VerifyAsync(client, token);

        await ShouldBeInvalidTokenAsync(response);
    }

    [Fact]
    public async Task FM24_ExpiredToken_Returns400InvalidToken()
    {
        using var client = AsAnonymous();
        var token = await RegisterAndGetTokenAsync(client, "expired@example.test");

        Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var response = await VerifyAsync(client, token);

        await ShouldBeInvalidTokenAsync(response);
        (await WithDbAsync(db => db.Users.Select(user => user.EmailConfirmed).SingleAsync(CancellationToken))).ShouldBeFalse();
    }

    [Fact]
    public async Task US02_UnknownToken_Returns400InvalidToken()
    {
        using var client = AsAnonymous();

        await ShouldBeInvalidTokenAsync(await VerifyAsync(client, "not-a-real-token"));
    }

    [Fact]
    public async Task US02_NewerVerificationEmail_InvalidatesOlderToken()
    {
        using var client = AsAnonymous();
        var first = await RegisterAndGetTokenAsync(client, "twice@example.test");
        var userId = await WithDbAsync(db => db.Users.Select(user => user.Id).SingleAsync(CancellationToken));
        await WithDbAsync(async db =>
        {
            db.OutboxMessages.Add(new Marketplace.Application.Ops.OutboxMessage
            {
                Type = Marketplace.Application.Common.OutboxMessageTypes.EmailVerificationRequested,
                Payload = $$"""{"userId":"{{userId}}"}""",
                CreatedAt = Time.GetUtcNow(),
                AvailableAt = Time.GetUtcNow(),
            });
            return await db.SaveChangesAsync(CancellationToken);
        });
        await ProcessOutboxAsync();
        var second = Email.LatestToken("twice@example.test", "verify-email");

        await ShouldBeInvalidTokenAsync(await VerifyAsync(client, first));
        (await VerifyAsync(client, second)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task US01_ExistingEmail_SendsNoticeWithoutToken()
    {
        using var client = AsAnonymous();
        await RegisterAndGetTokenAsync(client, "notice@example.test");

        await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "notice@example.test", password = "another long passphrase", displayName = "Buyer" }, CancellationToken);
        await ProcessOutboxAsync();

        var sent = Email.To("notice@example.test");
        var notice = sent[^1];
        notice.Subject.ShouldBe("Someone tried to register with your email");
        notice.TextBody.ShouldNotContain("token=");
    }

    [Fact]
    public async Task VerificationEmail_TokenIsStoredOnlyAsHash()
    {
        using var client = AsAnonymous();
        var token = await RegisterAndGetTokenAsync(client, "hash-only@example.test");

        var stored = await WithDbAsync(db => db.OneTimeTokens.Select(row => row.TokenHash).SingleAsync(CancellationToken));

        stored.ShouldNotBe(token);
        stored.Length.ShouldBe(64);
    }

    private async Task<string> RegisterAndGetTokenAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = "correct horse battery", displayName = "Buyer" }, CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await ProcessOutboxAsync();
        return Email.LatestToken(email, "verify-email");
    }

    private Task<int> ProcessOutboxAsync() => Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token }, TestContext.Current.CancellationToken);

    private static async Task ShouldBeInvalidTokenAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe("invalid_token");
    }
}
