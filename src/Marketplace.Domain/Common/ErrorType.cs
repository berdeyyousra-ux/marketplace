namespace Marketplace.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    PreconditionFailed,
    Locked,
    TooManyRequests,
    Failure,
    Unavailable,
}
