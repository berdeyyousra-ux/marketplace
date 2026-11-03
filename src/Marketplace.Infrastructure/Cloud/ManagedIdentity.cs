using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Marketplace.Infrastructure.Cloud;

// Section 15.5: in Azure each app signs in to PostgreSQL and Blob storage with its user-assigned managed identity, so
// neither a database password nor a storage key exists anywhere. Azure:ManagedIdentityClientId switches this on;
// without it (local development, tests) the connection strings are used as they are.
public static class ManagedIdentity
{
    public const string ClientIdKey = "Azure:ManagedIdentityClientId";

    public static TokenCredential? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientId = configuration[ClientIdKey];
        return string.IsNullOrWhiteSpace(clientId) ? null : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
}

// ConnectionStrings:BlobStorage is either a connection string (Azurite locally) or, in Azure, the account's blob
// endpoint URL, which is reached with the managed identity. Download links are then user-delegation SAS URLs.
public static class BlobServices
{
    public static BlobServiceClient Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException("ConnectionStrings:BlobStorage is not configured.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return new BlobServiceClient(value);
        }

        var credential = ManagedIdentity.FromConfiguration(configuration)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:BlobStorage is an endpoint URL, which is reached with a managed identity: set {ManagedIdentity.ClientIdKey}.");
        return new BlobServiceClient(endpoint, credential);
    }
}

// With a managed identity the database password is a Microsoft Entra access token. Tokens last about an hour, so the
// data source fetches a new one every 30 minutes, and retries after 10 seconds when that fails.
public sealed class ManagedIdentityDatabase(IConfiguration configuration) : IDisposable
{
    public const string TokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    private readonly Lazy<NpgsqlDataSource> _dataSource = new(() => Build(configuration));

    public NpgsqlDataSource DataSource => _dataSource.Value;

    // True once a DbContext has used the token data source.
    public bool IsCreated => _dataSource.IsValueCreated;

    public static NpgsqlDataSource Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");
        var credential = ManagedIdentity.FromConfiguration(configuration)
            ?? throw new InvalidOperationException($"{ManagedIdentity.ClientIdKey} is not configured.");

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) => (await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken)).Token,
            successRefreshInterval: TimeSpan.FromMinutes(30),
            failureRefreshInterval: TimeSpan.FromSeconds(10));
        return builder.Build();
    }

    public void Dispose()
    {
        if (_dataSource.IsValueCreated)
        {
            _dataSource.Value.Dispose();
        }
    }
}
