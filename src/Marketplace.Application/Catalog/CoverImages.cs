using System.Buffers.Binary;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Catalog;

// T-085: cover image rules. The type comes from the bytes, never from the client; the declared type must agree.
public static class CoverImageRules
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static readonly Error UnsupportedType =
        new("unsupported_file_type", "A cover must be a JPEG, PNG or WebP image.", ErrorType.Validation);

    public static readonly Error ContentMismatch =
        new("file_content_mismatch", "The image content does not match its declared type.", ErrorType.Validation);

    public static readonly Error Malformed =
        new("file_content_mismatch", "The image could not be read.", ErrorType.Validation);

    public static readonly Error TooLarge =
        new("file_too_large", "Cover images can be at most 5 MB.", ErrorType.Validation);

    private static readonly Dictionary<string, string> _extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
    };

    // The content type the file name declares, or null when it is not an allowed image type.
    public static string? DeclaredType(string fileName) => _extensions.GetValueOrDefault(Path.GetExtension(fileName).TrimStart('.'));

    // The content type the bytes are, or null when they are none of the allowed types.
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp" : null;
    }

    public static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        _ => "webp",
    };
}

// Removes metadata from an image without decoding it, so no pixel is changed and no image library is needed. Covers
// are served publicly, and a seller's photo can carry GPS coordinates, camera serials or names (T-085).
//   JPEG: drops APP1 to APP15 (EXIF, XMP, IPTC, vendor data) and COM segments. APP0 (JFIF) stays.
//   PNG:  drops eXIf, tEXt, zTXt, iTXt and tIME chunks.
//   WebP: drops EXIF and XMP chunks and clears their VP8X flags.
// Returns null when the structure cannot be followed; such a file is refused rather than stored with unknown content.
public static class ImageMetadata
{
    public static byte[]? Strip(byte[] image, string contentType)
    {
        ArgumentNullException.ThrowIfNull(image);
        return contentType switch
        {
            "image/jpeg" => StripJpeg(image),
            "image/png" => StripPng(image),
            "image/webp" => StripWebP(image),
            _ => null,
        };
    }

    private static byte[]? StripJpeg(byte[] image)
    {
        using var output = new MemoryStream(image.Length);
        output.Write(image, 0, 2);
        var position = 2;
        while (position + 4 <= image.Length)
        {
            if (image[position] != 0xFF)
            {
                return null;
            }

            var marker = image[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            // Start of scan: the entropy-coded data and everything after it are copied as they are.
            if (marker == 0xDA)
            {
                output.Write(image, position, image.Length - position);
                return output.ToArray();
            }

            // Markers without a length field.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write(image, position, 2);
                position += 2;
                continue;
            }

            var length = (image[position + 2] << 8) | image[position + 3];
            if (length < 2 || position + 2 + length > image.Length)
            {
                return null;
            }

            var isMetadata = marker is (>= 0xE1 and <= 0xEF) or 0xFE;
            if (!isMetadata)
            {
                output.Write(image, position, 2 + length);
            }

            position += 2 + length;
        }

        return null;
    }

    private static readonly string[] _pngMetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    private static byte[]? StripPng(byte[] image)
    {
        using var output = new MemoryStream(image.Length);
        output.Write(image, 0, 8);
        var position = 8;
        while (position + 12 <= image.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(position));
            if (length > int.MaxValue - 12 || position + 12 + (long)length > image.Length)
            {
                return null;
            }

            var type = System.Text.Encoding.ASCII.GetString(image, position + 4, 4);
            var chunkLength = 12 + (int)length;
            if (!_pngMetadataChunks.Contains(type, StringComparer.Ordinal))
            {
                output.Write(image, position, chunkLength);
            }

            position += chunkLength;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        return null;
    }

    private static byte[]? StripWebP(byte[] image)
    {
        if (image.Length < 12)
        {
            return null;
        }

        using var chunks = new MemoryStream(image.Length);
        var position = 12;
        while (position + 8 <= image.Length)
        {
            var type = System.Text.Encoding.ASCII.GetString(image, position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(position + 4));
            var padded = (long)length + (length % 2);
            if (position + 8 + padded > image.Length)
            {
                return null;
            }

            if (type is not ("EXIF" or "XMP "))
            {
                var start = chunks.Position;
                chunks.Write(image, position, 8 + (int)padded);
                if (type == "VP8X" && length >= 1)
                {
                    // Clear the EXIF (0x08) and XMP (0x04) flags now that those chunks are gone.
                    chunks.Position = start + 8;
                    var flags = (byte)(image[position + 8] & ~0x0C);
                    chunks.WriteByte(flags);
                    chunks.Position = chunks.Length;
                }
            }

            position += 8 + (int)padded;
        }

        if (position != image.Length)
        {
            return null;
        }

        var body = chunks.ToArray();
        var result = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(4 + body.Length));
        "WEBP"u8.CopyTo(result.AsSpan(8));
        body.CopyTo(result, 12);
        return result;
    }
}

// The public URL of a product's cover, or null when it has none.
public static class CoverUrls
{
    public static Uri? For(Abstractions.IPublicImageStorage images, string? key, DateTimeOffset? updatedAt)
    {
        ArgumentNullException.ThrowIfNull(images);
        return key is null ? null : images.PublicUrl(key, updatedAt ?? DateTimeOffset.UnixEpoch);
    }

    public static async Task<IReadOnlyDictionary<Guid, Uri?>> ForProductsAsync(
        Abstractions.IAppDbContext db, Abstractions.IPublicImageStorage images, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ids = productIds.Distinct().ToList();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Products.Where(product => ids.Contains(product.Id)).Select(product => new { product.Id, product.CoverImageKey, product.CoverImageUpdatedAt }),
            cancellationToken);
        return rows.ToDictionary(row => row.Id, row => For(images, row.CoverImageKey, row.CoverImageUpdatedAt));
    }
}

public sealed record SetCoverImageCommand(Guid ProductId, string Key, long SizeBytes, string ContentType);

public sealed record CoverImageResponse(Uri CoverImageUrl, DateTimeOffset UpdatedAt);

// After the stripped image is stored in the public container: one transaction points the product at it, audits,
// and queues the replaced cover's deletion (T-085).
public sealed class SetCoverImageCommandHandler(
    Abstractions.IAppDbContext db, ProductOwnership ownership, Ops.AuditTrail audit, Abstractions.IOutbox outbox, Abstractions.ICurrentUser currentUser,
    Abstractions.IPublicImageStorage images, TimeProvider timeProvider)
    : Abstractions.ICommandHandler<SetCoverImageCommand, CoverImageResponse>
{
    public async Task<Result<CoverImageResponse>> HandleAsync(SetCoverImageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await ownership.FindOwnedAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var set = product.SetCover(command.Key, now);
        if (set.IsFailure)
        {
            return set.Error;
        }

        audit.Record("product.cover_updated", Ops.AuditActorType.User, currentUser.UserId, "Product", product.Id, new { command.SizeBytes, command.ContentType });
        if (set.Value is { } previous)
        {
            outbox.Enqueue(Common.OutboxMessageTypes.DeleteCoverImage, new DeleteBlobMessage(previous));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CoverImageResponse(images.PublicUrl(command.Key, now), now);
    }
}

public sealed class DeleteCoverImageHandler(Abstractions.IPublicImageStorage images) : Abstractions.IOutboxHandler
{
    private static readonly System.Text.Json.JsonSerializerOptions _json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public string MessageType => Common.OutboxMessageTypes.DeleteCoverImage;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = System.Text.Json.JsonSerializer.Deserialize<DeleteBlobMessage>(payload, _json)
            ?? throw new System.Text.Json.JsonException("The payload has no key.");
        await images.DeleteAsync(message.Key, cancellationToken);
    }
}
