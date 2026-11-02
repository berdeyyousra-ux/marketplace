using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    /// <summary>Returns the signed-in user's account, roles and seller profile, if any (EP-09).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetAsync([FromServices] IQueryHandler<MeQuery, MeResponse> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(new MeQuery(), cancellationToken), me => Ok(me));
    }
}
