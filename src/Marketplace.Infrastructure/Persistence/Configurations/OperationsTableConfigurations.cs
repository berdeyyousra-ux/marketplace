using Marketplace.Application.Ops;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Provider).HasMaxLength(30);
        builder.Property(e => e.ProviderEventId).HasMaxLength(100);
        builder.Property(e => e.EventType).HasMaxLength(100);
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        builder.Property(e => e.Attempts).WithDatabaseDefault("0");
        builder.HasAlternateKey(e => new { e.Provider, e.ProviderEventId }).HasName("ux_payment_events_provider_event");
        builder.HasIndex(e => e.ReceivedAt).HasDatabaseName("ix_payment_events_unprocessed").HasFilter("processed_at IS NULL");
    }
}

internal sealed class AiSuggestionConfiguration : IEntityTypeConfiguration<AiSuggestion>
{
    public void Configure(EntityTypeBuilder<AiSuggestion> builder)
    {
        builder.ToTable("ai_suggestions", table => table.HasCheckConstraint(
            "ck_ai_suggestions_status", "validation_status IN ('Valid','InvalidOutput','ProviderError','Skipped')"));
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Model).HasMaxLength(100);
        builder.Property(s => s.PromptVersion).HasMaxLength(30);
        builder.Property(s => s.InputFacts).HasColumnType("jsonb");
        builder.Property(s => s.ParsedOutput).HasColumnType("jsonb");
        builder.Property(s => s.ValidationStatus).AsText();
        builder.Property(s => s.Error).HasMaxLength(1000);
        builder.HasOne<RefundRequest>().WithMany().HasForeignKey(s => s.RefundRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.RefundRequestId, s.CreatedAt })
            .HasDatabaseName("ix_ai_suggestions_refund_request_id")
            .IsDescending(false, true);
    }
}

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys", table => table.HasCheckConstraint(
            "ck_idempotency_keys_status", "status IN ('InProgress','Completed')"));
        builder.HasKey(k => new { k.UserId, k.Key });
        builder.Property(k => k.Key).HasMaxLength(64);
        builder.Property(k => k.Method).HasMaxLength(10);
        builder.Property(k => k.Path).HasMaxLength(200);
        builder.Property(k => k.RequestHash).HasMaxLength(64).IsFixedLength();
        builder.Property(k => k.Status).AsText();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(k => k.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.Attempts).WithDatabaseDefault("0");
        builder.HasIndex(m => new { m.AvailableAt, m.CreatedAt })
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log", table => table.HasCheckConstraint(
            "ck_audit_log_actor_type", "actor_type IN ('User','System','Worker','Webhook')"));
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.ActorType).AsText();
        builder.Property(a => a.Action).HasMaxLength(100);
        builder.Property(a => a.EntityType).HasMaxLength(50);
        builder.Property(a => a.Details).HasColumnType("jsonb");
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.Id })
            .HasDatabaseName("ix_audit_log_entity")
            .IsDescending(false, false, true);
        builder.HasIndex(a => a.OccurredAt).HasDatabaseName("ix_audit_log_occurred_at").IsDescending();
    }
}
