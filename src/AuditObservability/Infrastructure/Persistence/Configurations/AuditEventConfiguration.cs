using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="AuditEventEntity"/>. The
/// table is created as a TimescaleDB hypertable via raw SQL in
/// the initial migration (Timescale-specific catalog calls live
/// outside the EF model so plain PG tooling stays usable).
/// </summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_events");

        // Composite key including occurred_at: TimescaleDB requires the
        // partitioning column to be part of every unique index on a
        // hypertable (TS103). audit_id stays first so it reads as the
        // logical identity; occurred_at is carried for the partition.
        builder.HasKey(auditEvent => new { auditEvent.Id, auditEvent.OccurredAt });

        builder.Property(auditEvent => auditEvent.Id)
            .HasColumnName("audit_id")
            .HasConversion(id => id.Value, value => AuditEventIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(auditEvent => auditEvent.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.Fab)
            .HasColumnName("fab_id")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(
                fab => fab == null ? null : fab.Value,
                value => value == null ? null : FabIdentifier.From(value));

        builder.Property(auditEvent => auditEvent.EventKind)
            .HasColumnName("event_kind")
            .HasMaxLength(EventKind.MaximumLength)
            .HasConversion(kind => kind.Value, value => EventKind.From(value))
            .IsRequired();

        builder.Property(auditEvent => auditEvent.ResourceKind)
            .HasColumnName("resource_kind")
            .HasMaxLength(32)
            .HasConversion(
                kind => kind == null ? null : kind.Value,
                value => value == null ? null : ResourceKind.From(value));

        builder.Property(auditEvent => auditEvent.ResourceIdentifier)
            .HasColumnName("resource_identifier")
            .HasMaxLength(ResourceIdentifier.MaximumLength)
            .HasConversion(
                resource => resource == null ? null : resource.Value,
                value => value == null ? null : ResourceIdentifier.From(value));

        builder.Property(auditEvent => auditEvent.Actor)
            .HasColumnName("actor_identifier")
            .HasConversion(actor => actor.Value, value => value == Guid.Empty ? ActorIdentifier.System : ActorIdentifier.From(value))
            .IsRequired();

        builder.Property(auditEvent => auditEvent.ActorUsername)
            .HasColumnName("actor_username")
            .HasMaxLength(255);

        builder.Property(auditEvent => auditEvent.EventIdentifier)
            .HasColumnName("event_identifier")
            .HasConversion(eventIdentifier => eventIdentifier.Value, value => EventIdentifier.From(value))
            .IsRequired();

        builder.Property(auditEvent => auditEvent.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.PayloadSizeBytes)
            .HasColumnName("payload_size_bytes")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();

        // Idempotency: Wolverine at-least-once redeliveries are absorbed
        // via INSERT ... ON CONFLICT (event_identifier, occurred_at). The
        // unique index carries occurred_at because TimescaleDB forbids a
        // unique index that omits the partitioning column (TS103); since a
        // given event always carries the same occurred_at, the pair still
        // uniquely dedups redeliveries.
        builder.HasIndex(auditEvent => new { auditEvent.EventIdentifier, auditEvent.OccurredAt })
            .HasDatabaseName("ux_audit_event_identifier")
            .IsUnique();

        // Cross-cutting search.
        builder.HasIndex(auditEvent => new { auditEvent.Actor, auditEvent.OccurredAt })
            .HasDatabaseName("ix_audit_actor_occurred");

        builder.HasIndex(auditEvent => new { auditEvent.Fab, auditEvent.OccurredAt })
            .HasDatabaseName("ix_audit_fab_occurred");

        builder.HasIndex(auditEvent => new { auditEvent.EventKind, auditEvent.OccurredAt })
            .HasDatabaseName("ix_audit_kind_occurred");

        // Per-resource timeline.
        builder.HasIndex(auditEvent => new { auditEvent.ResourceKind, auditEvent.ResourceIdentifier, auditEvent.OccurredAt })
            .HasDatabaseName("ix_audit_resource_occurred");
    }
}
