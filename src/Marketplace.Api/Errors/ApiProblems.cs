using System.Diagnostics;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Errors;

// Section 8.1: every error is application/problem+json with type, title, status, detail, code and traceId.
public static class ApiProblems
{
    public const string TypeBase = "https://marketplace.dev/errors/";

    public static readonly Error InternalError =
        new("internal_error", "An unexpected error occurred.", ErrorType.Failure);

    public static readonly Error ServiceUnavailable =
        new("service_unavailable", "A required service is temporarily unavailable. Try again shortly.", ErrorType.Unavailable);

    public static readonly Error InvalidRequest =
        new("invalid_request", "The request could not be read.", ErrorType.Validation);

    public static ProblemDetails Create(HttpContext context, Error error, int? statusOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        var problem = new ProblemDetails
        {
            Type = TypeBase + error.Code,
            Title = Humanize(error.Code),
            Status = statusOverride ?? ErrorStatusCodes.For(error),
            Detail = error.Message,
        };
        Enrich(context, problem, error.Code);
        return problem;
    }

    // Adds the code and traceId extensions to any problem, including framework-generated ones.
    public static void Enrich(HttpContext context, ProblemDetails problem, string code)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);

        problem.Type ??= TypeBase + code;
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
    }

    public static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "invalid_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status413PayloadTooLarge => "file_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_file_type",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        StatusCodes.Status503ServiceUnavailable => "service_unavailable",
        _ => status >= 500 ? "internal_error" : "invalid_request",
    };

    private static string Humanize(string code) =>
        code.Length == 0 ? code : char.ToUpperInvariant(code[0]) + code[1..].Replace('_', ' ');
}
