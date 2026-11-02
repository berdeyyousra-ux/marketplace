using Marketplace.Application.Abstractions;
using Marketplace.Application.Ops;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Idempotency;

// Section 10.1 steps 3 to 5, with the current time from TimeProvider instead of now() (changelog 2026-11-02).
public sealed class IdempotencyStore(AppDbContext db, TimeProvider timeProvider) : IIdempotencyStore
{
    public static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(24);

    public async Task<IdempotencyOutcome> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var lockedUntil = now + LockDuration;
        var expiresAt = now + KeyLifetime;
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (user_id, key, method, path, request_hash, status, created_at, locked_until, expires_at)
            VALUES ({request.UserId}, {request.Key}, {request.Method}, {request.Path}, {request.RequestHash}, 'InProgress', {now}, {lockedUntil}, {expiresAt})
            ON CONFLICT (user_id, key) DO NOTHING
            """, cancellationToken);
        if (inserted == 1)
        {
            return new IdempotencyOutcome(IdempotencyClaim.Proceed);
        }

        var existing = await db.IdempotencyKeys.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == request.UserId && row.Key == request.Key, cancellationToken);
        if (existing is null)
        {
            // Released between our insert and read: treat as a new key on the client's retry.
            return new IdempotencyOutcome(IdempotencyClaim.InProgress);
        }

        if (existing.RequestHash != request.RequestHash || existing.Method != request.Method || existing.Path != request.Path)
        {
            return new IdempotencyOutcome(IdempotencyClaim.Mismatch);
        }

        if (existing.Status == IdempotencyKeyStatus.Completed)
        {
            return new IdempotencyOutcome(IdempotencyClaim.Replay, existing.ResponseStatus, existing.ResponseBody);
        }

        if (existing.LockedUntil > now)
        {
            return new IdempotencyOutcome(IdempotencyClaim.InProgress);
        }

        // A crashed request left the key locked: exactly one retry can take it over.
        var taken = await db.IdempotencyKeys
            .Where(row => row.UserId == request.UserId && row.Key == request.Key && row.Status == IdempotencyKeyStatus.InProgress && row.LockedUntil <= now)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LockedUntil, lockedUntil), cancellationToken);
        return new IdempotencyOutcome(taken == 1 ? IdempotencyClaim.Proceed : IdempotencyClaim.InProgress);
    }

    public Task CompleteAsync(Guid userId, string key, int status, string body, CancellationToken cancellationToken) =>
        db.IdempotencyKeys
            .Where(row => row.UserId == userId && row.Key == key)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Status, IdempotencyKeyStatus.Completed)
                    .SetProperty(row => row.ResponseStatus, status)
                    .SetProperty(row => row.ResponseBody, body),
                cancellationToken);

    public Task ReleaseAsync(Guid userId, string key, CancellationToken cancellationToken) =>
        db.IdempotencyKeys.Where(row => row.UserId == userId && row.Key == key).ExecuteDeleteAsync(cancellationToken);
}
