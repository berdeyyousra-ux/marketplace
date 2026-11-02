namespace Marketplace.Application.Ops;

public enum AuditActorType
{
    User,
    System,
    Worker,
    Webhook,
}

public enum IdempotencyKeyStatus
{
    InProgress,
    Completed,
}

// Append-only audit row (R-08, section 7.3). Written in the same transaction as the change it records.
public sealed class AuditLogEntry
{
    public long Id { get; private set; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required AuditActorType ActorType { get; init; }

    public Guid? ActorUserId { get; init; }

    public required string Action { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    public string? Details { get; init; }

    public string? CorrelationId { get; init; }
}

// Transactional outbox row (D-23). The worker claims and processes these (section 10.3, T-030).
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public string? CorrelationId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset AvailableAt { get; init; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset? DeadLetteredAt { get; private set; }
}

// Stored response for an Idempotency-Key request (section 10.1, T-032).
public sealed class IdempotencyKey
{
    public required Guid UserId { get; init; }

    public required string Key { get; init; }

    public required string Method { get; init; }

    public required string Path { get; init; }

    public required string RequestHash { get; init; }

    public IdempotencyKeyStatus Status { get; private set; }

    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset LockedUntil { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
