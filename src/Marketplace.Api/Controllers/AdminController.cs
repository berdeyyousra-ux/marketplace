using Marketplace.Api.Auth;
using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Application.Refunds;
using Marketplace.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

// EP-74 to EP-77 and EP-80 to EP-83 (US-32, US-33, US-34).
[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
[Authorize(Policy = AuthenticationSetup.RequireAdmin)]
public sealed class AdminController : ControllerBase
{
    /// <summary>Net sales, fees, paid orders, refund rate, order value percentiles and the daily order funnel for a period (US-33).</summary>
    [HttpGet("reports/overview")]
    [ProducesResponseType(typeof(OverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> OverviewAsync(
        [FromQuery] OverviewQuery query, [FromServices] IQueryHandler<OverviewQuery, OverviewResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), overview => Ok(overview));
    }

    /// <summary>Sellers ranked by net sales over the last N days (US-33, R-08).</summary>
    [HttpGet("reports/top-sellers")]
    [ProducesResponseType(typeof(ItemList<TopSeller>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> TopSellersAsync(
        [FromQuery] TopSellersQuery query, [FromServices] IQueryHandler<TopSellersQuery, ItemList<TopSeller>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), sellers => Ok(sellers));
    }

    /// <summary>Refund rates per seller and for the most refunded products over the last N days (US-33, R-04).</summary>
    [HttpGet("reports/refund-rates")]
    [ProducesResponseType(typeof(RefundRatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RefundRatesAsync(
        [FromQuery] RefundRatesQuery query, [FromServices] IQueryHandler<RefundRatesQuery, RefundRatesResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), rates => Ok(rates));
    }

    /// <summary>Repeat-buyer cohorts by month of first purchase (US-33, R-03).</summary>
    [HttpGet("reports/repeat-buyers")]
    [ProducesResponseType(typeof(ItemList<RepeatBuyerCohort>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RepeatBuyersAsync(
        [FromQuery] RepeatBuyersQuery query, [FromServices] IQueryHandler<RepeatBuyersQuery, ItemList<RepeatBuyerCohort>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), cohorts => Ok(cohorts));
    }

    /// <summary>Refund requests, oldest first, with the policy verdict and the AI suggestion's summary or its absence. Keyset paginated (US-30).</summary>
    [HttpGet("refund-requests")]
    [ProducesResponseType(typeof(Page<AdminRefundSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RefundRequestsAsync(
        [FromQuery] AdminRefundRequestsQuery query, [FromServices] IQueryHandler<AdminRefundRequestsQuery, Page<AdminRefundSummary>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }

    /// <summary>One refund request in full: buyer text, the facts sent to the model, the policy verdict, the AI suggestion and the history (US-30).</summary>
    [HttpGet("refund-requests/{id:guid}")]
    [ProducesResponseType(typeof(AdminRefundDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RefundRequestAsync(
        Guid id, [FromServices] IQueryHandler<AdminRefundRequestQuery, AdminRefundDetail> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new AdminRefundRequestQuery(id), cancellationToken), detail => Ok(detail));
    }

    /// <summary>Approves or rejects a refund with a note. Approving an out-of-window request needs an override reason. Needs an Idempotency-Key (US-31).</summary>
    [HttpPost("refund-requests/{id:guid}/decision")]
    [Idempotent]
    [ProducesResponseType(typeof(RefundDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> DecideRefundAsync(
        Guid id, RefundDecisionRequest request, [FromServices] ICommandHandler<DecideRefundCommand, RefundDecisionResponse> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        var command = new DecideRefundCommand(id, request.Decision, request.Note, request.OverrideReason);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), decision => Ok(decision));
    }

    /// <summary>Retries a Failed refund: it returns to Executing and is processed again (FM-12).</summary>
    [HttpPost("refund-requests/{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RetryRefundAsync(
        Guid id, [FromServices] ICommandHandler<RetryRefundCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new RetryRefundCommand(id), cancellationToken), _ => Accepted());
    }

    /// <summary>Removes a product with a reason. It leaves public lists; buyers keep their downloads (US-32).</summary>
    [HttpPost("products/{id:guid}/remove")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RemoveProductAsync(
        Guid id, RemoveProductRequest request, [FromServices] ICommandHandler<RemoveProductCommand, ProductResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new RemoveProductCommand(id, request.Reason), cancellationToken), product => Ok(product));
    }

    /// <summary>Audit entries, newest first, filtered by entity, actor and time. Keyset paginated (US-34).</summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(Page<AuditLogItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> AuditLogAsync(
        [FromQuery] AuditLogQuery query, [FromServices] IQueryHandler<AuditLogQuery, Page<AuditLogItem>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }

    /// <summary>Dead-lettered outbox messages, most recent first. Keyset paginated (US-34).</summary>
    [HttpGet("outbox/dead-letters")]
    [ProducesResponseType(typeof(Page<DeadLetter>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> DeadLettersAsync(
        [FromQuery] DeadLettersQuery query, [FromServices] IQueryHandler<DeadLettersQuery, Page<DeadLetter>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }

    /// <summary>Clears a dead-lettered message and resets its attempts, so the Worker processes it again (US-34).</summary>
    [HttpPost("outbox/{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RetryAsync(
        Guid id, [FromServices] ICommandHandler<RetryOutboxCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new RetryOutboxCommand(id), cancellationToken), _ => Accepted());
    }
}
