using Marketplace.Api.Auth;
using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    /// <summary>Returns a product. Non-published products are visible only to the owner, buyers and admins (US-21).</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetAsync(Guid id, [FromServices] IQueryHandler<ProductQuery, ProductResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new ProductQuery(id), cancellationToken), Respond);
    }

    /// <summary>Creates a draft product (US-11).</summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateAsync(
        ProductDetails details, [FromServices] ICommandHandler<CreateProductCommand, ProductResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new CreateProductCommand(details), cancellationToken), product =>
        {
            Response.Headers.ETag = ETags.Format(product.Version);
            return Created($"/api/v1/products/{product.Id}", product);
        });
    }

    /// <summary>Updates a product. Requires If-Match with the current ETag (US-13).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [RequireIfMatch]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> UpdateAsync(
        Guid id, ProductDetails details, [FromServices] ICommandHandler<UpdateProductCommand, ProductResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new UpdateProductCommand(id, this.IfMatchVersion(), details), cancellationToken), Respond);
    }

    /// <summary>Publishes a product. It needs an uploaded file (US-14).</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [RequireIfMatch]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public Task<IActionResult> PublishAsync(Guid id, [FromServices] ICommandHandler<ChangeProductStatusCommand, ProductResponse> handler, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, publish: true, handler, cancellationToken);

    /// <summary>Unpublishes a product. Existing buyers keep their access (US-14).</summary>
    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [RequireIfMatch]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    public Task<IActionResult> UnpublishAsync(Guid id, [FromServices] ICommandHandler<ChangeProductStatusCommand, ProductResponse> handler, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, publish: false, handler, cancellationToken);

    private async Task<IActionResult> ChangeStatusAsync(
        Guid id, bool publish, ICommandHandler<ChangeProductStatusCommand, ProductResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new ChangeProductStatusCommand(id, this.IfMatchVersion(), publish), cancellationToken), Respond);
    }

    // Only the owner gets the ETag, since only the owner can edit (EP-22).
    private OkObjectResult Respond(ProductResponse product)
    {
        if (product.IsOwner)
        {
            Response.Headers.ETag = ETags.Format(product.Version);
        }

        return Ok(product);
    }
}
