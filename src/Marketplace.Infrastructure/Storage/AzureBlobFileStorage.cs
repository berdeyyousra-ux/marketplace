using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Storage;

// Section 16 "Storage" configuration. The connection string is ConnectionStrings:BlobStorage.
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ContainerName { get; set; } = "product-files";

    public int DownloadUrlMinutes { get; set; } = 5;
}

// D-26. Uploads stream in blocks, so no file is held in memory. The SDK's default exponential retry applies
// (section 10.2), with a 120-second timeout for uploads and 10 seconds otherwise.
public sealed class AzureBlobFileStorage : IFileStorage
{
    private static readonly TimeSpan _uploadTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan _otherTimeout = TimeSpan.FromSeconds(10);

    private readonly BlobContainerClient _container;
    private readonly Lazy<Task> _ensureContainer;

    public AzureBlobFileStorage(IConfiguration configuration, IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = configuration.GetConnectionString("BlobStorage")
            ?? throw new InvalidOperationException("ConnectionStrings:BlobStorage is not configured.");
        _container = new BlobServiceClient(connectionString).GetBlobContainerClient(options.Value.ContainerName);
        _ensureContainer = new Lazy<Task>(() => _container.CreateIfNotExistsAsync(PublicAccessType.None));
    }

    public BlobContainerClient Container => _container;

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await _ensureContainer.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_uploadTimeout);
        await _container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = Azure.ETag.All },
            },
            timeout.Token);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_otherTimeout);
        await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: timeout.Token);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_otherTimeout);
        return await _container.GetBlobClient(key).ExistsAsync(timeout.Token);
    }
}

public sealed class NoopFileScanner : IFileScanner
{
    public Task<bool> IsCleanAsync(string key, CancellationToken cancellationToken) => Task.FromResult(true);
}
