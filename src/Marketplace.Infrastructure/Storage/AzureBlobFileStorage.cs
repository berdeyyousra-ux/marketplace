using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Storage;

// Section 16 "Storage" configuration. ConnectionStrings:BlobStorage is a connection string, or in Azure the blob
// endpoint URL reached with the managed identity (Cloud/ManagedIdentity.cs).
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ContainerName { get; set; } = "product-files";

    public int DownloadUrlMinutes { get; set; } = 5;

    // T-085: product covers live in their own public-read container, never in ContainerName, which is private.
    public string CoverContainerName { get; set; } = "product-covers";

    // Optional public address for covers (a CDN, or a host the browser can reach when the API sees storage under a
    // container-network name). By default the blob endpoint itself.
    public Uri? CoverPublicBaseUrl { get; set; }
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

        _container = Cloud.BlobServices.Create(configuration).GetBlobContainerClient(options.Value.ContainerName);
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

    // Shared-key SAS when the connection string carries an account key (Azurite, dev); otherwise a user
    // delegation SAS, which works with a managed identity (section 15).
    public async Task<Uri> CreateDownloadUrlAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(key);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
            ContentDisposition = ContentDisposition(fileName),
        };
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_otherTimeout);
        var service = _container.GetParentBlobServiceClient();
        var delegationKey = await service.GetUserDelegationKeyAsync(startsOn: null, expiresAt, timeout.Token);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(delegationKey.Value, service.AccountName) }.ToUri();
    }

    // RFC 6266: an ASCII fallback plus the UTF-8 name.
    private static string ContentDisposition(string fileName)
    {
        var ascii = new string([.. fileName.Select(character => character is < (char)0x20 or > (char)0x7E or '"' or '\\' ? '_' : character)]);
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}

public sealed class NoopFileScanner : IFileScanner
{
    public Task<bool> IsCleanAsync(string key, CancellationToken cancellationToken) => Task.FromResult(true);
}

// T-085: cover images. The container allows anonymous reads of blobs (not listing), and every blob is immutable:
// a replaced cover gets a new key, and the URL carries the update time as a version for good measure.
public sealed class AzureBlobPublicImageStorage : IPublicImageStorage
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly BlobContainerClient _container;
    private readonly Uri? _publicBase;
    private readonly Lazy<Task> _ensureContainer;

    public AzureBlobPublicImageStorage(IConfiguration configuration, IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.Value.CoverContainerName, options.Value.ContainerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage:CoverContainerName must differ from the private Storage:ContainerName.");
        }

        _container = Cloud.BlobServices.Create(configuration).GetBlobContainerClient(options.Value.CoverContainerName);
        _publicBase = options.Value.CoverPublicBaseUrl;
        _ensureContainer = new Lazy<Task>(() => _container.CreateIfNotExistsAsync(PublicAccessType.Blob));
    }

    public BlobContainerClient Container => _container;

    public async Task UploadAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        await _ensureContainer.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await _container.GetBlobClient(key).UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "public, max-age=31536000, immutable" },
                Conditions = new BlobRequestConditions { IfNoneMatch = Azure.ETag.All },
            },
            timeout.Token);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: timeout.Token);
    }

    public Uri PublicUrl(string key, DateTimeOffset version)
    {
        var blob = _container.GetBlobClient(key).Uri;
        var location = _publicBase is null ? blob : new Uri(_publicBase, $"{_container.Name}/{key}");
        return new UriBuilder(location) { Query = $"v={version.ToUnixTimeMilliseconds()}" }.Uri;
    }
}
