namespace Marketplace.Application.Abstractions;

public enum IdempotencyClaim
{
    // The key is new, or a crashed request's lock was taken over: run the request.
    Proceed,

    // A completed response exists for this exact request: replay it.
    Replay,

    // The same key is being processed right now: 409 idempotency_in_progress.
    InProgress,

    // The key was used for a different request: 422 idempotency_key_reuse.
    Mismatch,
}

public sealed record IdempotencyRequest(Guid UserId, string Key, string Method, string Path, string RequestHash);

public sealed record IdempotencyOutcome(IdempotencyClaim Claim, int? ResponseStatus = null, string? ResponseBody = null);

// Section 10.1. Times come from TimeProvider (changelog 2026-11-02).
public interface IIdempotencyStore
{
    Task<IdempotencyOutcome> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(Guid userId, string key, int status, string body, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid userId, string key, CancellationToken cancellationToken);
}
