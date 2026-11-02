using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Catalog;

// Section 9.2 upload rules: allowed extensions, a fixed content-type map, magic bytes, and file name cleanup.
public static class FileRules
{
    public const long MaxBytes = 100L * 1024 * 1024;
    public const int SniffBytes = 8192;

    public static readonly Error UnsupportedType =
        new("unsupported_file_type", "This file type is not allowed.", ErrorType.Validation);

    public static readonly Error ContentMismatch =
        new("file_content_mismatch", "The file content does not match its extension.", ErrorType.Validation);

    public static readonly Error TooLarge =
        new("file_too_large", "Files can be at most 100 MB.", ErrorType.Validation);

    // The stored content type always comes from this map, never from the client.
    private static readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["zip"] = "application/zip",
        ["epub"] = "application/epub+zip",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["mp3"] = "audio/mpeg",
        ["mp4"] = "video/mp4",
        ["txt"] = "text/plain",
        ["md"] = "text/markdown",
        ["otf"] = "font/otf",
        ["ttf"] = "font/ttf",
    };

    public static string? ContentTypeFor(string extension) => _contentTypes.GetValueOrDefault(extension);

    public static string Extension(string fileName) => Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

    public static bool MatchesMagicBytes(string extension, ReadOnlySpan<byte> head) => extension.ToLowerInvariant() switch
    {
        "pdf" => head.StartsWith("%PDF"u8),
        "zip" or "epub" => head.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        "png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        "jpg" or "jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "mp3" => head.StartsWith("ID3"u8) || head.StartsWith(new byte[] { 0xFF, 0xFB }) || head.StartsWith(new byte[] { 0xFF, 0xF3 }) || head.StartsWith(new byte[] { 0xFF, 0xF2 }),
        "mp4" => head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8),
        "otf" => head.StartsWith("OTTO"u8),
        "ttf" => head.StartsWith(new byte[] { 0x00, 0x01, 0x00, 0x00 }),
        "txt" or "md" => !head.Contains((byte)0),
        _ => false,
    };

    // Strips any path, control characters and invalid file name characters, and caps the length at 255.
    public static string SanitizeFileName(string? fileName, string extension)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var cleaned = new StringBuilder(name.Length);
        foreach (var character in name.Where(character => !char.IsControl(character) && !Path.GetInvalidFileNameChars().Contains(character)))
        {
            cleaned.Append(character);
        }

        var result = cleaned.ToString().Trim().TrimStart('.');
        if (result.Length == 0)
        {
            result = "file." + extension;
        }

        return result.Length <= 255 ? result : result[..(255 - extension.Length - 1)] + "." + extension;
    }
}

// EP-25 and US-12.
public sealed record UploadTargetQuery(Guid ProductId);

public sealed record AttachProductFileCommand(Guid ProductId, string Key, string FileName, long SizeBytes, string ContentType, string Sha256);

public sealed record DeleteBlobMessage(string Key);

// Checks ownership and status before any bytes are stored, so non-owners never write to blob storage.
public sealed class UploadTargetQueryHandler(ProductOwnership ownership) : IQueryHandler<UploadTargetQuery, Guid>
{
    public async Task<Result<Guid>> HandleAsync(UploadTargetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await ownership.FindOwnedAsync(query.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        return product.Status == ProductStatus.Removed
            ? DomainErrors.InvalidState("A removed product cannot receive a file.")
            : product.Id;
    }
}

// After the blob is stored: one transaction updates the file columns and audits, and the previous file's
// blob is queued for deletion (section 9.2).
public sealed class AttachProductFileCommandHandler(
    IAppDbContext db, ProductOwnership ownership, AuditTrail audit, IOutbox outbox, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<AttachProductFileCommand, ProductFileResponse>
{
    public async Task<Result<ProductFileResponse>> HandleAsync(AttachProductFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await ownership.FindOwnedAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var file = ProductFile.Create(command.Key, command.FileName, command.SizeBytes, command.ContentType, command.Sha256);
        if (file.IsFailure)
        {
            return file.Error;
        }

        var previousKey = product.File?.Key;
        var attached = product.AttachFile(file.Value, timeProvider.GetUtcNow());
        if (attached.IsFailure)
        {
            return attached.Error;
        }

        audit.Record("product.file_uploaded", AuditActorType.User, currentUser.UserId, "Product", product.Id,
            new { command.FileName, command.SizeBytes, command.ContentType, command.Sha256 });
        if (previousKey is not null)
        {
            outbox.Enqueue(OutboxMessageTypes.DeleteBlob, new DeleteBlobMessage(previousKey));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ProductFileResponse(file.Value.Name, file.Value.SizeBytes, file.Value.ContentType, file.Value.Sha256);
    }
}

public sealed class DeleteBlobHandler(IFileStorage storage) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.DeleteBlob;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<DeleteBlobMessage>(payload, _json) ?? throw new JsonException("The payload has no key.");
        await storage.DeleteAsync(message.Key, cancellationToken);
    }
}
