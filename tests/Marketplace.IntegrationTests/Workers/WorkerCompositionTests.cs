using Marketplace.Application;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Jobs;
using Marketplace.Infrastructure.Outbox;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Workers;

// T-060: the Worker registers only background services (AddApplicationBackground). This builds the same
// composition with the validation Development applies at startup, so a missing registration fails here.
public sealed class WorkerCompositionTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task WorkerServices_AllResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = Factory.DatabaseConnectionString,
                ["ConnectionStrings:BlobStorage"] = Factory.BlobConnectionString,
            })
            .Build());
        services.AddLogging();
        services.AddApplicationBackground();
        services.AddInfrastructure();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        provider.GetRequiredService<OutboxProcessor>().ShouldNotBeNull();
        scope.ServiceProvider.GetServices<IOutboxHandler>().ShouldNotBeEmpty();
        scope.ServiceProvider.GetRequiredService<OrderExpiryJob>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<PaymentReconciliationJob>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<CleanupJob>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<SellerRevenueViewRefreshJob>().ShouldNotBeNull();
    }
}
