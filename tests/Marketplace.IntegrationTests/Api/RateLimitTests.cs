using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

// T-028 and section 11.4. Each test gets its own host with limiting switched on, so counters start at zero.
public sealed class RateLimitTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task T028_EleventhLoginInAMinute_Returns429WithRetryAfter()
    {
        using var client = LimitedClient();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            (await LoginAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await LoginAsync(client);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe("rate_limited");
    }

    [Fact]
    public async Task Refresh_ThirtyFirstInAMinute_Returns429()
    {
        using var client = LimitedClient();

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            (await RefreshAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await RefreshAsync(client)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Global_HundredAndTwentyFirstRequest_Returns429()
    {
        using var client = LimitedClient();

        for (var attempt = 1; attempt <= 120; attempt++)
        {
            (await client.GetAsync("/api/v1/categories", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.GetAsync("/api/v1/categories", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Health_IsNeverRateLimited()
    {
        using var client = LimitedClient();

        for (var attempt = 1; attempt <= 150; attempt++)
        {
            (await client.GetAsync("/health/live", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AuthPolicy_DoesNotSpendTheGlobalBudget()
    {
        using var client = LimitedClient();
        for (var attempt = 1; attempt <= 11; attempt++)
        {
            await LoginAsync(client);
        }

        var other = await client.GetAsync("/api/v1/categories", CancellationToken);

        other.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private HttpClient LimitedClient() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Enabled", "true"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "not the right passphrase" }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "not-a-real-token" }, TestContext.Current.CancellationToken);
}
