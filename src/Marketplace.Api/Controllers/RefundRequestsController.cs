using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Refunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/refund-requests")]
[Produces("application/json")]
public sealed class RefundRequestsController : ControllerBase
{
    /// <summary>Requests a refund for a purchased item. The policy decides at once; AI analysis runs afterwards. Needs an Idempotency-Key (US-28).</summary>
    [HttpPost]
    [Authorize]
    [Idempotent]
    [ProducesResponseType(typeof(RefundRequestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateAsync(
        CreateRefundRequestCommand command, [FromServices] ICommandHandler<CreateRefundRequestCommand, RefundRequestResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), request => Created("/api/v1/refund-requests/mine", request));
    }

    /// <summary>Lists the caller's refund requests and their status, newest first. Keyset paginated (US-28).</summary>
    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(Page<MyRefundRequest>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> MineAsync(
        [FromQuery] MyRefundRequestsQuery query, [FromServices] IQueryHandler<MyRefundRequestsQuery, Page<MyRefundRequest>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }
}
