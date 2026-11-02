using System.Text.Json;
using Marketplace.Application.Abstractions;

namespace Marketplace.Application.Ops;

// R-08: audit rows are added to the same unit of work as the change they describe, and saved with it.
public sealed class AuditTrail(IAppDbContext db, TimeProvider timeProvider, ICorrelationContext correlation)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Record(
        string action, AuditActorType actorType, Guid? actorUserId = null, string? entityType = null, Guid? entityId = null, object? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        db.AuditLog.Add(new AuditLogEntry
        {
            OccurredAt = timeProvider.GetUtcNow(),
            ActorType = actorType,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details, details.GetType(), _json),
            CorrelationId = correlation.CorrelationId,
        });
    }
}

// The caller's network details, recorded on refresh tokens (section 7.2) and nowhere else.
public interface IClientContext
{
    System.Net.IPAddress? IpAddress { get; }

    string? UserAgent { get; }
}
