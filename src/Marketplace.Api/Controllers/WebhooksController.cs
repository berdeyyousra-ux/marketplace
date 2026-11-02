using Marketplace.Api.Infrastructure;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Payments;
using Marketplace.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Controllers;

[ApiController]
[Produces("application/json")]
public sealed partial class WebhooksController(ILogger<WebhooksController> logger) : ControllerBase
{
    /// <summary>Receives a signed payment provider webhook. Verifies and stores it; processing is asynchronous (EP-40).</summary>
    [HttpPost("api/v1/webhooks/payments")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Webhook)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> ReceiveAsync(
        [FromServices] ICommandHandler<ReceiveWebhookCommand, Unit> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // The raw body is read unchanged, because the signature covers the exact bytes.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var headers = Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await handler.HandleAsync(new ReceiveWebhookCommand(headers, buffer.ToArray()), cancellationToken);
        if (result.IsFailure)
        {
            LogRejected(logger, result.Error.Code);
        }

        return this.ToActionResult(result, _ => Ok());
    }

    /// <summary>Development only: completes a fake payment and delivers its signed webhook (EP-99).</summary>
    [HttpPost("dev/fake-payments/{providerPaymentId}/complete")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> CompleteFakePaymentAsync(
        string providerPaymentId,
        [FromServices] IHostEnvironment environment,
        [FromServices] IOptions<PaymentOptions> options,
        [FromServices] FakePaymentProvider fake,
        [FromServices] ICommandHandler<ReceiveWebhookCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fake);
        ArgumentNullException.ThrowIfNull(handler);

        if (!environment.IsDevelopment() || options.Value.Provider != PaymentOptions.FakeName)
        {
            return NotFound();
        }

        var (headers, body) = fake.CompletePayment(providerPaymentId);
        return this.ToActionResult(await handler.HandleAsync(new ReceiveWebhookCommand(headers, body), cancellationToken), _ => Ok());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected payment webhook: {Code}")]
    private static partial void LogRejected(ILogger logger, string code);
}
