using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Cloud;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Cloud;

// T-061a: in Azure the apps reach PostgreSQL and Blob storage with their managed identity (section 15.5); locally the
// connection strings are used as they are. No token is requested here: credentials only fetch one when a connection
// or a request is made.
public sealed class ManagedIdentityTests
{
    private const string _clientId = "00000000-0000-0000-0000-000000000001";
    private const string _endpoint = "https://stmarketplace.blob.core.windows.net/";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    [Fact]
    public void Blob_ConnectionString_IsUsedAsIs()
    {
        var client = BlobServices.Create(Config(("ConnectionStrings:BlobStorage", "UseDevelopmentStorage=true")));

        client.AccountName.ShouldBe("devstoreaccount1");
        client.CanGenerateAccountSasUri.ShouldBeTrue();
    }

    [Fact]
    public void Blob_EndpointUrl_UsesTheManagedIdentity_AndHasNoAccountKey()
    {
        var client = BlobServices.Create(Config(("ConnectionStrings:BlobStorage", _endpoint), (ManagedIdentity.ClientIdKey, _clientId)));

        client.Uri.ShouldBe(new Uri(_endpoint));
        // No key: download links are user-delegation SAS URLs signed with a key the identity requests.
        client.CanGenerateAccountSasUri.ShouldBeFalse();
    }

    [Fact]
    public void Blob_EndpointUrl_WithoutAnIdentity_FailsWithAClearMessage()
    {
        var error = Should.Throw<InvalidOperationException>(() => BlobServices.Create(Config(("ConnectionStrings:BlobStorage", _endpoint))));

        error.Message.ShouldContain(ManagedIdentity.ClientIdKey);
    }

    [Fact]
    public void Database_WithoutAnIdentity_UsesTheConnectionString()
    {
        var (connectionString, tokenSourceUsed) = Connection(Config(
            ("ConnectionStrings:Database", "Host=localhost;Database=marketplace;Username=marketplace;Password=devpassword")));

        connectionString.ShouldContain("Password=devpassword");
        tokenSourceUsed.ShouldBeFalse();
    }

    [Fact]
    public void Database_WithAnIdentity_UsesTheTokenDataSource_AndNoPassword()
    {
        var (connectionString, tokenSourceUsed) = Connection(Config(
            ("ConnectionStrings:Database", "Host=psql.example.postgres.database.azure.com;Database=marketplace;Username=id-marketplace-api;SSL Mode=Require"),
            (ManagedIdentity.ClientIdKey, _clientId)));

        connectionString.ShouldNotContain("Password");
        tokenSourceUsed.ShouldBeTrue();
    }

    // The connection EF hands out, and whether the managed-identity data source made it. Nothing is opened.
    private static (string ConnectionString, bool TokenSourceUsed) Connection(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        return (connection.ConnectionString, provider.GetRequiredService<ManagedIdentityDatabase>().IsCreated);
    }
}
