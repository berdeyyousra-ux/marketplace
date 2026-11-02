using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marketplace.Api.Errors;

// Section 4.5 step 2 and D-19: the one place unhandled exceptions become ProblemDetails.
// Exception details go to the log only, never to the response.
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails, IDbExceptionTranslator translator, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var error = Classify(exception);
        if (error.Type == ErrorType.Failure)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method);
        }
        else
        {
            LogHandled(logger, exception.GetType().Name, error.Code);
        }

        var problem = ApiProblems.Create(httpContext, error, error == _payloadTooLarge ? StatusCodes.Status413PayloadTooLarge : null);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private Error Classify(Exception exception) => exception switch
    {
        DbUpdateException update when translator.Translate(update) is { } translated => translated,
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => _payloadTooLarge,
        BadHttpRequestException => ApiProblems.InvalidRequest,
        // D-42: transient database failures are not retried and surface as 503.
        NpgsqlException { IsTransient: true } or TimeoutException => ApiProblems.ServiceUnavailable,
        DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => ApiProblems.ServiceUnavailable,
        _ => ApiProblems.InternalError,
    };

    private static readonly Error _payloadTooLarge = new("file_too_large", "The request body is too large.", ErrorType.Validation);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} request")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed with {ExceptionType}, returned as {Code}")]
    private static partial void LogHandled(ILogger logger, string exceptionType, string code);
}
