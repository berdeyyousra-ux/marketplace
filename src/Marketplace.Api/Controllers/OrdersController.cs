using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Orders;
using Marketplace.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>Creates a Pending order for 1 to 10 products at today's prices. Needs an Idempotency-Key (US-22).</summary>
    [HttpPost]
    [Authorize]
    [Idempotent]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateAsync(
        CreateOrderCommand command, [FromServices] ICommandHandler<CreateOrderCommand, OrderResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), order => Created($"/api/v1/orders/{order.Id}", order));
    }

    /// <summary>Lists the caller's own orders, newest first. Keyset paginated (US-25).</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(Page<OrderSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> ListAsync(
        [FromQuery] MyOrdersQuery query, [FromServices] IQueryHandler<MyOrdersQuery, Page<OrderSummary>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(query, cancellationToken), page => Ok(page));
    }

    /// <summary>Returns one order with its items and latest payment. Visible to its buyer and to admins (US-25).</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(OrderDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetAsync(Guid id, [FromServices] IQueryHandler<OrderQuery, OrderDetail> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new OrderQuery(id), cancellationToken), order => Ok(order));
    }

    /// <summary>Cancels the caller's Pending order. Paid orders cannot be cancelled (US-27).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CancelAsync(Guid id, [FromServices] ICommandHandler<CancelOrderCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new CancelOrderCommand(id), cancellationToken), _ => NoContent());
    }

    /// <summary>Starts paying for a Pending order and returns the provider's client secret. Needs an Idempotency-Key (US-23).</summary>
    [HttpPost("{id:guid}/payment")]
    [Authorize]
    [Idempotent]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> PayAsync(
        Guid id, [FromServices] ICommandHandler<CreatePaymentCommand, PaymentResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new CreatePaymentCommand(id), cancellationToken), payment => Ok(payment));
    }
}
