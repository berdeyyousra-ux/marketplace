namespace Marketplace.Application.Abstractions;

// D-23 and D-44: handlers enqueue side effects explicitly, inside the same unit of work as the state change.
// The row is written by the caller's SaveChangesAsync, so the effect happens only if the transaction commits.
public interface IOutbox
{
    void Enqueue(string type, object payload);
}

// One handler per outbox message type. Handlers must be idempotent (section 9.10).
public interface IOutboxHandler
{
    string MessageType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);

    // Called once when the message is dead-lettered, for example to mark a refund Failed (section 9.8).
    Task OnDeadLetterAsync(string payload, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}

// The correlation ID of the current request or outbox message (section 4.5).
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
