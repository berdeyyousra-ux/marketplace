using Marketplace.Api.Auth;
using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed partial class ProductFilesController(IFileStorage storage, IFileScanner scanner, ILogger<ProductFilesController> logger) : ControllerBase
{
    // The 100 MB file plus room for the multipart framing. The global request limit is 1 MB (section 9.2).
    private const long _requestLimit = FileRules.MaxBytes + (64 * 1024);
    private const long _coverRequestLimit = CoverImageRules.MaxBytes + (64 * 1024);

    /// <summary>Uploads the product's file as multipart field "file". Streamed to storage, at most 100 MB (US-12).</summary>
    [HttpPost("{id:guid}/file")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [RequestSizeLimit(_requestLimit)]
    [DisableFormValueModelBinding]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProductFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> UploadAsync(
        Guid id,
        [FromServices] IQueryHandler<UploadTargetQuery, Guid> target,
        [FromServices] ICommandHandler<AttachProductFileCommand, ProductFileResponse> attach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(attach);

        var allowed = await target.HandleAsync(new UploadTargetQuery(id), cancellationToken);
        if (allowed.IsFailure)
        {
            return this.Problem(allowed.Error);
        }

        var section = await FindFileSectionAsync(cancellationToken);
        if (section is null)
        {
            return this.Problem(Errors.ApiProblems.InvalidRequest);
        }

        var (section1, disposition) = section.Value;
        var extension = FileRules.Extension(disposition.FileName.Value ?? string.Empty);
        var contentType = FileRules.ContentTypeFor(extension);
        if (contentType is null)
        {
            return ProblemWithStatus(FileRules.UnsupportedType, StatusCodes.Status415UnsupportedMediaType);
        }

        // Sniff the start before storing anything.
        var head = new byte[FileRules.SniffBytes];
        var headLength = await ReadHeadAsync(section1.Body, head, cancellationToken);
        if (!FileRules.MatchesMagicBytes(extension, head.AsSpan(0, headLength)))
        {
            return this.Problem(FileRules.ContentMismatch);
        }

        var key = $"products/{id}/{Guid.CreateVersion7()}";
        await using var inspecting = new InspectingStream(head, headLength, section1.Body, FileRules.MaxBytes);
        try
        {
            await storage.UploadAsync(key, inspecting, contentType, cancellationToken);
        }
        catch (FileTooLargeException)
        {
            await DeleteQuietlyAsync(key);
            return ProblemWithStatus(FileRules.TooLarge, StatusCodes.Status413PayloadTooLarge);
        }

        if (!await scanner.IsCleanAsync(key, cancellationToken))
        {
            await DeleteQuietlyAsync(key);
            return this.Problem(FileRules.ContentMismatch);
        }

        var fileName = FileRules.SanitizeFileName(disposition.FileName.Value, extension);
        try
        {
            var result = await attach.HandleAsync(
                new AttachProductFileCommand(id, key, fileName, inspecting.BytesRead, contentType, inspecting.Sha256Hex()), cancellationToken);
            if (result.IsFailure)
            {
                await DeleteQuietlyAsync(key);
            }

            return this.ToActionResult(result, file => Ok(file));
        }
        catch
        {
            // Best effort: rare orphaned blobs are accepted (section 9.2).
            await DeleteQuietlyAsync(key);
            throw;
        }
    }

    /// <summary>Sets the product's cover image from multipart field "file": JPEG, PNG or WebP, at most 5 MB. Metadata such as EXIF is
    /// removed before the image is stored, because covers are public (T-085).</summary>
    [HttpPut("{id:guid}/cover")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [RequestSizeLimit(_coverRequestLimit)]
    [DisableFormValueModelBinding]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CoverImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> UploadCoverAsync(
        Guid id,
        [FromServices] IQueryHandler<UploadTargetQuery, Guid> target,
        [FromServices] ICommandHandler<SetCoverImageCommand, CoverImageResponse> setCover,
        [FromServices] IPublicImageStorage images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(setCover);
        ArgumentNullException.ThrowIfNull(images);

        var allowed = await target.HandleAsync(new UploadTargetQuery(id), cancellationToken);
        if (allowed.IsFailure)
        {
            return this.Problem(allowed.Error);
        }

        var section = await FindFileSectionAsync(cancellationToken);
        if (section is null)
        {
            return this.Problem(Errors.ApiProblems.InvalidRequest);
        }

        var declared = CoverImageRules.DeclaredType(section.Value.Disposition.FileName.Value ?? string.Empty);
        if (declared is null)
        {
            return ProblemWithStatus(CoverImageRules.UnsupportedType, StatusCodes.Status415UnsupportedMediaType);
        }

        // At most 5 MB is ever held: reading stops one byte past the limit.
        var bytes = await ReadAtMostAsync(section.Value.Section.Body, CoverImageRules.MaxBytes + 1, cancellationToken);
        if (bytes.Length > CoverImageRules.MaxBytes)
        {
            return ProblemWithStatus(CoverImageRules.TooLarge, StatusCodes.Status413PayloadTooLarge);
        }

        var actual = CoverImageRules.Sniff(bytes);
        if (actual is null)
        {
            return ProblemWithStatus(CoverImageRules.UnsupportedType, StatusCodes.Status415UnsupportedMediaType);
        }

        if (actual != declared)
        {
            return this.Problem(CoverImageRules.ContentMismatch);
        }

        var stripped = ImageMetadata.Strip(bytes, actual);
        if (stripped is null)
        {
            return this.Problem(CoverImageRules.Malformed);
        }

        var key = $"covers/{id}/{Guid.CreateVersion7()}.{CoverImageRules.Extension(actual)}";
        await images.UploadAsync(key, stripped, actual, cancellationToken);
        try
        {
            var result = await setCover.HandleAsync(new SetCoverImageCommand(id, key, stripped.Length, actual), cancellationToken);
            if (result.IsFailure)
            {
                await DeleteCoverQuietlyAsync(images, key);
            }

            return this.ToActionResult(result, cover => Ok(cover));
        }
        catch
        {
            await DeleteCoverQuietlyAsync(images, key);
            throw;
        }
    }

    private static async Task<byte[]> ReadAtMostAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < limit && (read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task DeleteCoverQuietlyAsync(IPublicImageStorage images, string key)
    {
        try
        {
            await images.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogOrphanedBlob(logger, exception, key);
        }
    }

    private async Task<(MultipartSection Section, ContentDispositionHeaderValue Disposition)?> FindFileSectionAsync(CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return null;
        }

        var reader = new MultipartReader(boundary, Request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                && disposition.Name.Value == "file"
                && disposition.FileName.HasValue)
            {
                return (section, disposition);
            }
        }

        return null;
    }

    private static async Task<int> ReadHeadAsync(Stream body, byte[] head, CancellationToken cancellationToken)
    {
        var total = 0;
        int read;
        while (total < head.Length && (read = await body.ReadAsync(head.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total;
    }

    private ObjectResult ProblemWithStatus(Domain.Common.Error error, int status) =>
        new(Errors.ApiProblems.Create(HttpContext, error, status)) { StatusCode = status, ContentTypes = { "application/problem+json" } };

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogOrphanedBlob(logger, exception, key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete rejected upload {BlobKey}; it is left orphaned")]
    private static partial void LogOrphanedBlob(ILogger logger, Exception exception, string blobKey);
}
