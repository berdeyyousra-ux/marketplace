using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Marketplace.IntegrationTests.Api;

public sealed class ApiHostTests : IAsyncLifetime
{
    // Nothing listens on port 1, so connections are refused immediately.
    private const string _unreachableDatabase = "Host=localhost;Port=1;Database=none;Username=none;Password=none;Timeout=2";
    private const string _correlationHeader = "X-Correlation-Id";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() =>
        await _postgres.DisposeAsync();

    [Fact]
    public async Task HealthLive_DatabaseDown_Returns200()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DatabaseUp_Returns200()
    {
        await using var factory = new ApiFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DatabaseDown_Returns503()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CorrelationId_HeaderMissing_GeneratesOne()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Guid.TryParse(response.Headers.GetValues(_correlationHeader).Single(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task CorrelationId_ValidHeader_IsEchoed()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(_correlationHeader, "client-request_1.2");

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues(_correlationHeader).Single().ShouldBe("client-request_1.2");
    }

    [Theory]
    [InlineData("contains spaces")]
    [InlineData("{\"injected\":true}")]
    public async Task CorrelationId_UnsafeHeader_IsReplaced(string unsafeValue)
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(_correlationHeader, unsafeValue);

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Guid.TryParse(response.Headers.GetValues(_correlationHeader).Single(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task CorrelationId_OverlongHeader_IsReplaced()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(_correlationHeader, new string('a', 65));

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Guid.TryParse(response.Headers.GetValues(_correlationHeader).Single(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task SecurityHeaders_AnyResponse_ArePresent()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe("default-src 'none'");
    }

    [Fact]
    public async Task Cors_ConfiguredOrigin_IsAllowed()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("http://localhost:3000");
    }

    [Fact]
    public async Task Cors_UnknownOrigin_IsNotAllowed()
    {
        await using var factory = new ApiFactory(_unreachableDatabase);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            // Testing, not the default Development, so a developer's user secrets cannot change the result.
            builder
                .UseEnvironment("Testing")
                .UseSetting("Ai:Provider", "Fake")
                .UseSetting("ConnectionStrings:Database", connectionString)
                // Section 16 requires it at startup; nothing in these tests reaches blob storage.
                .UseSetting("ConnectionStrings:BlobStorage", "UseDevelopmentStorage=true")
                .UseSetting("Jwt:SigningKey", Marketplace.IntegrationTests.Fixtures.MarketplaceWebFactory.TestSigningKey);
    }
}
