using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Fulfillment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class DownloadsController : ControllerBase
{
    /// <summary>Issues a 5-minute signed download URL for a purchased item and counts the download (US-26).</summary>
    [HttpPost("downloads/{orderItemId:guid}/link")]
    [Authorize]
    [EnableRateLimiting(RateLimiting.Download)]
    [ProducesResponseType(typeof(DownloadLink), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateLinkAsync(
        Guid orderItemId, [FromServices] ICommandHandler<CreateDownloadLinkCommand, DownloadLink> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new CreateDownloadLinkCommand(orderItemId), cancellationToken), link => Ok(link));
    }

    /// <summary>Lists the caller's purchased products with each download grant's status. Keyset paginated (US-26).</summary>
    [HttpGet("library")]
    [Authorize]
    [ProducesResponseType(typeof(Page<LibraryItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> LibraryAsync(
        [FromQuery] LibraryQuery query, [FromServices] IQueryHandler<LibraryQuery, Page<LibraryItem>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }
}
