using Marketplace.Infrastructure.Outbox;
using Serilog.Context;

namespace Marketplace.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string _headerName = "X-Correlation-Id";
    private const int _maxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        string? supplied = context.Request.Headers[_headerName];
        var correlationId = IsValid(supplied) ? supplied! : Guid.CreateVersion7().ToString();

        context.Response.Headers[_headerName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (AmbientCorrelationContext.Begin(correlationId))
        {
            await next(context);
        }
    }

    // Supplied values reach logs and database columns, so only short, plain identifiers are accepted.
    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= _maxLength }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
