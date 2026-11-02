using Marketplace.Api.Auth;
using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/sellers")]
[Produces("application/json")]
public sealed class SellersController : ControllerBase
{
    /// <summary>Creates the caller's seller profile and grants the Seller role. Refresh tokens afterwards to use it (US-10).</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateAsync(
        BecomeSellerCommand command, [FromServices] ICommandHandler<BecomeSellerCommand, SellerProfileResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), profile => Created("/api/v1/sellers/me", profile));
    }

    /// <summary>Returns the caller's seller profile (EP-11).</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthenticationSetup.RequireSeller)]
    [ProducesResponseType(typeof(SellerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetMineAsync(
        [FromServices] IQueryHandler<SellerQuery, SellerProfileResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new SellerQuery(), cancellationToken), profile => Ok(profile));
    }
}

[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public sealed class CategoriesController : ControllerBase
{
    /// <summary>Lists product categories in display order. Cacheable for 5 minutes (EP-20).</summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromServices] IQueryHandler<CategoriesQuery, IReadOnlyList<CategoryResponse>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new CategoriesQuery(), cancellationToken), categories => Ok(categories));
    }
}
