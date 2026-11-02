namespace Marketplace.Application.Abstractions;

// T-085: product cover images. Unlike IFileStorage (private, signed downloads), this container is public-read: anyone,
// signed in or not, may fetch a cover by its URL. The two must never share a container (docs/frontend-design-plan.md).
public interface IPublicImageStorage
{
    Task UploadAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    // Deleting a blob that does not exist counts as success.
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    // A long-lived, cacheable URL. The version changes when the cover is replaced, so caches never serve a stale one.
    Uri PublicUrl(string key, DateTimeOffset version);
}
