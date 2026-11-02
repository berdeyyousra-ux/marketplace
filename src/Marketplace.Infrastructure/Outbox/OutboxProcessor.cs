using System.Diagnostics;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Outbox;

public sealed record ClaimedOutboxMessage(Guid Id, string Type, string Payload, string? CorrelationId, int Attempts);

// Section 10.3, with the current time passed in from TimeProvider instead of now() (changelog 2026-11-02).
public sealed partial class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<OutboxProcessor> logger)
{
    public const int BatchSize = 20;
    public const int MaxAttempts = 8;
    public const int MaxConcurrency = 4;
    public const int LastErrorMaxLength = 2000;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(60);

    // Claims and processes one batch. Returns the number of messages claimed.
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(cancellationToken);
        await Parallel.ForEachAsync(
            claimed,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency },
            async (message, _) => await ProcessAsync(message, cancellationToken));
        return claimed.Count;
    }

    // Takes a 5-minute lease so no long transaction is held while handlers run. A crash leaves the lease
    // in place and the message becomes available again when it expires.
    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + Lease;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Database.SqlQuery<ClaimedOutboxMessage>($"""
            UPDATE outbox_messages
            SET attempts = attempts + 1, available_at = {leaseUntil}
            WHERE id IN (
              SELECT id FROM outbox_messages
              WHERE processed_at IS NULL AND dead_lettered_at IS NULL AND available_at <= {now}
              ORDER BY available_at, created_at
              LIMIT {BatchSize}
              FOR UPDATE SKIP LOCKED)
            RETURNING id, type, payload::text AS payload, correlation_id, attempts
            """).ToListAsync(cancellationToken);
    }

    public static TimeSpan Backoff(int attempts, double jitter)
    {
        var seconds = Math.Min(10 * Math.Pow(2, attempts - 1), TimeSpan.FromHours(1).TotalSeconds);
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    private async Task ProcessAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
    {
        using var correlation = AmbientCorrelationContext.Begin(message.CorrelationId);
        var started = Stopwatch.GetTimestamp();
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().FirstOrDefault(candidate => candidate.MessageType == message.Type);

        string outcome;
        if (handler is null)
        {
            outcome = "dead-lettered";
            await DeadLetterAsync(message, null, $"No handler is registered for message type {message.Type}.", cancellationToken);
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HandlerTimeout);
            try
            {
                await handler.HandleAsync(message.Payload, timeout.Token);
                await UpdateAsync(message.Id, cancellationToken, processed: true);
                outcome = "processed";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down: keep the lease so the message is retried after it expires.
                outcome = "abandoned at shutdown";
            }
            catch (Exception exception) when (exception is NonRetryableException or JsonException)
            {
                outcome = "dead-lettered";
                await DeadLetterAsync(message, handler, Describe(exception), cancellationToken);
            }
            catch (Exception exception)
            {
                if (message.Attempts >= MaxAttempts)
                {
                    outcome = "dead-lettered";
                    await DeadLetterAsync(message, handler, Describe(exception), cancellationToken);
                }
                else
                {
                    outcome = "retry scheduled";
                    var retryAt = timeProvider.GetUtcNow() + Backoff(message.Attempts, 0.8 + (Random.Shared.NextDouble() * 0.4));
                    await UpdateAsync(message.Id, cancellationToken, retryAt: retryAt, error: Describe(exception));
                }
            }
        }

        var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogProcessed(logger, message.Type, message.Id, message.Attempts, durationMs, outcome, message.CorrelationId);
    }

    private async Task DeadLetterAsync(ClaimedOutboxMessage message, IOutboxHandler? handler, string error, CancellationToken cancellationToken)
    {
        await UpdateAsync(message.Id, cancellationToken, deadLettered: true, error: error);
        if (handler is null)
        {
            return;
        }

        try
        {
            await handler.OnDeadLetterAsync(message.Payload, error, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeadLetterHookFailed(logger, exception, message.Type, message.Id);
        }
    }

    private async Task UpdateAsync(
        Guid id, CancellationToken cancellationToken, bool processed = false, bool deadLettered = false, DateTimeOffset? retryAt = null, string? error = null)
    {
        var now = timeProvider.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.OutboxMessages.Where(message => message.Id == id);

        if (processed)
        {
            await row.ExecuteUpdateAsync(set => set.SetProperty(message => message.ProcessedAt, now), cancellationToken);
        }
        else if (deadLettered)
        {
            await row.ExecuteUpdateAsync(
                set => set.SetProperty(message => message.DeadLetteredAt, now).SetProperty(message => message.LastError, error), cancellationToken);
        }
        else
        {
            await row.ExecuteUpdateAsync(
                set => set.SetProperty(message => message.AvailableAt, retryAt!.Value).SetProperty(message => message.LastError, error), cancellationToken);
        }
    }

    // Only the exception type and message are stored: never the payload (section 10.3).
    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().Name}: {exception.Message}";
        return text.Length > LastErrorMaxLength ? text[..LastErrorMaxLength] : text;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageType} {MessageId} attempt {Attempt} {Outcome} in {DurationMs:0.0} ms (correlation {CorrelationId})")]
    private static partial void LogProcessed(ILogger logger, string messageType, Guid messageId, int attempt, double durationMs, string outcome, string? correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-letter hook failed for outbox message {MessageType} {MessageId}")]
    private static partial void LogDeadLetterHookFailed(ILogger logger, Exception exception, string messageType, Guid messageId);
}
