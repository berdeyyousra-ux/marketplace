using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace Marketplace.IntegrationTests.Fixtures;

// Section 14.2: one PostgreSQL and one Azurite container per test collection, migrations applied once.
// Fakes for payments, email and the LLM are added by the tasks that introduce those services.
public sealed class MarketplaceWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset StartTime = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    // Azurite lags behind the Azure SDK's newest storage API version, so the version check is skipped.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    // A fresh random key per test run; no signing key is committed (section 11.3).
    public static readonly string TestSigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public const string AdminEmail = "admin@marketplace.test";

    // A fresh random admin password per run, long enough for the section 11.1 rules.
    public static readonly string AdminPassword = "admin-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));

    public FakeTimeProvider Time { get; } = new(StartTime);

    public string DatabaseConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());

        // Migrate before the app starts, because startup seeds the admin into these tables (section 7.6).
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
        await db.Database.MigrateAsync();
        _ = Services;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:BlobStorage", _azurite.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Seed:Admin:Email", AdminEmail);
        builder.UseSetting("Seed:Admin:Password", AdminPassword);

        // Every test request shares one address, so limits would leak between tests. RateLimitTests re-enable them.
        builder.UseSetting("RateLimiting:Enabled", "false");

        // Section 10.2 policies with short real-time values, so FM-09 and FM-10 run in well under a second. The breaker
        // never opens here, because tests that simulate failures share the provider; ResiliencePipelineTests cover it.
        foreach (var profile in new[] { "PaymentsApi", "PaymentsWorker" })
        {
            builder.UseSetting($"Resilience:{profile}:AttemptTimeout", "00:00:00.300");
            builder.UseSetting($"Resilience:{profile}:TotalTimeout", "00:00:03");
            builder.UseSetting($"Resilience:{profile}:BaseDelay", "00:00:00.010");
            builder.UseSetting($"Resilience:{profile}:MinimumThroughput", "100000");
        }
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<FakeEmailSender>());
            services.AddSingleton<TestOutboxHandler>();
            services.AddSingleton<IOutboxHandler>(provider => provider.GetRequiredService<TestOutboxHandler>());
        });
    }
}
