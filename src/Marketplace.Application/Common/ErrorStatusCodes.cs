using Marketplace.Domain.Common;

namespace Marketplace.Application.Common;

// The ErrorType to HTTP status table from section 6.1, kept free of ASP.NET Core types so it can be unit tested.
public static class ErrorStatusCodes
{
    public static int For(ErrorType type) => type switch
    {
        ErrorType.Validation => 422,
        ErrorType.NotFound => 404,
        ErrorType.Conflict => 409,
        ErrorType.Unauthorized => 401,
        ErrorType.Forbidden => 403,
        ErrorType.PreconditionFailed => 412,
        ErrorType.Locked => 423,
        ErrorType.TooManyRequests => 429,
        ErrorType.Failure => 500,
        ErrorType.Unavailable => 503,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type."),
    };
}
