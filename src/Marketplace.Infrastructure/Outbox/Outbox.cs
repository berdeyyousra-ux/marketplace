using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Ops;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.Outbox;

public sealed class Outbox(AppDbContext db, TimeProvider timeProvider, ICorrelationContext correlation) : IOutbox
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Enqueue(string type, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        var now = timeProvider.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload, payload.GetType(), _json),
            CorrelationId = correlation.CorrelationId,
            CreatedAt = now,
            AvailableAt = now,
        });
    }
}

// Holds the correlation ID for the current async flow: set by the Api middleware per request, and by the
// outbox processor per message, so effects queued by a handler stay traceable to the original request.
public sealed class AmbientCorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public string? CorrelationId => _current.Value;

    public static IDisposable Begin(string? correlationId)
    {
        var previous = _current.Value;
        _current.Value = correlationId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
