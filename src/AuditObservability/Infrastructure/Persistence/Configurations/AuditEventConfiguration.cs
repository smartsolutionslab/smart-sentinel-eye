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
        builder.HasKey(a => new { a.Id, a.OccurredAt });

        builder.Property(a => a.Id)
            .HasColumnName("audit_id")
            .HasConversion(id => id.Value, value => AuditEventIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(a => a.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(a => a.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired();

        builder.Property(a => a.Fab)
            .HasColumnName("fab_id")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(
                f => f == null ? null : f.Value,
                v => v == null ? null : FabIdentifier.From(v));

        builder.Property(a => a.EventKind)
            .HasColumnName("event_kind")
            .HasMaxLength(EventKind.MaximumLength)
            .HasConversion(k => k.Value, v => EventKind.From(v))
            .IsRequired();

        builder.Property(a => a.ResourceKind)
            .HasColumnName("resource_kind")
            .HasMaxLength(32)
            .HasConversion(
                k => k == null ? null : k.Value,
                v => v == null ? null : ResourceKind.From(v));

        builder.Property(a => a.ResourceIdentifier)
            .HasColumnName("resource_identifier")
            .HasMaxLength(ResourceIdentifier.MaximumLength)
            .HasConversion(
                r => r == null ? null : r.Value,
                v => v == null ? null : ResourceIdentifier.From(v));

        builder.Property(a => a.Actor)
            .HasColumnName("actor_identifier")
            .HasConversion(a => a.Value, v => v == Guid.Empty ? ActorIdentifier.System : ActorIdentifier.From(v))
            .IsRequired();

        builder.Property(a => a.ActorUsername)
            .HasColumnName("actor_username")
            .HasMaxLength(255);

        builder.Property(a => a.EventIdentifier)
            .HasColumnName("event_identifier")
            .HasConversion(e => e.Value, v => EventIdentifier.From(v))
            .IsRequired();

        builder.Property(a => a.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.PayloadSizeBytes)
            .HasColumnName("payload_size_bytes")
            .IsRequired();

        builder.Property(a => a.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();

        // Idempotency: Wolverine at-least-once redeliveries are absorbed
        // via INSERT ... ON CONFLICT (event_identifier, occurred_at). The
        // unique index carries occurred_at because TimescaleDB forbids a
        // unique index that omits the partitioning column (TS103); since a
        // given event always carries the same occurred_at, the pair still
        // uniquely dedups redeliveries.
        builder.HasIndex(a => new { a.EventIdentifier, a.OccurredAt })
            .HasDatabaseName("ux_audit_event_identifier")
            .IsUnique();

        // Cross-cutting search.
        builder.HasIndex(a => new { a.Actor, a.OccurredAt })
            .HasDatabaseName("ix_audit_actor_occurred");

        builder.HasIndex(a => new { a.Fab, a.OccurredAt })
            .HasDatabaseName("ix_audit_fab_occurred");

        builder.HasIndex(a => new { a.EventKind, a.OccurredAt })
            .HasDatabaseName("ix_audit_kind_occurred");

        // Per-resource timeline.
        builder.HasIndex(a => new { a.ResourceKind, a.ResourceIdentifier, a.OccurredAt })
            .HasDatabaseName("ix_audit_resource_occurred");
    }
}
