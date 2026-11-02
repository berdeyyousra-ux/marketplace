namespace Marketplace.Application.Abstractions;

// D-26: product files live in blob storage (Azure Blob, Azurite locally).
public interface IFileStorage
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    // Deleting a blob that does not exist counts as success (section 9.10, DeleteBlob).
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    // Section 9.7: a read-only signed URL that downloads as an attachment named fileName.
    Task<Uri> CreateDownloadUrlAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

// Section 9.2: every upload is scanned. v1 binds a no-op scanner.
public interface IFileScanner
{
    Task<bool> IsCleanAsync(string key, CancellationToken cancellationToken);
}
