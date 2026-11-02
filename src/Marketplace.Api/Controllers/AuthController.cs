using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Registers an account. Always returns 202, whether or not the email is already registered (US-01).</summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RegisterAsync(
        RegisterCommand command, [FromServices] ICommandHandler<RegisterCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), _ => Accepted());
    }

    /// <summary>Signs in with email and password, returning a 15-minute access token and a 14-day refresh token (US-03).</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenPair), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status423Locked, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> LoginAsync(
        LoginCommand command, [FromServices] ICommandHandler<LoginCommand, TokenPair> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), tokens => Ok(tokens));
    }

    /// <summary>Exchanges a refresh token for a new token pair. A reused token signs out its whole family (US-04).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenPair), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> RefreshAsync(
        RefreshCommand command, [FromServices] ICommandHandler<RefreshCommand, TokenPair> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), tokens => Ok(tokens));
    }

    /// <summary>Signs out the session the refresh token belongs to. Always returns 204 (US-05).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(
        LogoutCommand command, [FromServices] ICommandHandler<LogoutCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), _ => NoContent());
    }

    /// <summary>Confirms an email address with the single-use token from the verification email (US-02).</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> VerifyEmailAsync(
        VerifyEmailCommand command, [FromServices] ICommandHandler<VerifyEmailCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return this.ToActionResult(await handler.HandleAsync(command, cancellationToken), _ => NoContent());
    }
}
