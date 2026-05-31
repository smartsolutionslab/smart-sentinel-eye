using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.Event;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="EventAggregate"/> envelope (spec
/// 006 FR-001 / FR-012). The actual partitioning DDL is in the raw-SQL
/// migration; EF Core just maps the columns.
///
/// <para>
/// Composite primary key (fab_id, event_id, ingested_at) is required
/// because Postgres list-partitioning + range-partitioning needs the
/// partition keys in the PK. The unique <c>(fab_id, event_id)</c>
/// constraint is added separately by the migration to enforce
/// hybrid-idempotency (FR-002).
/// </para>
/// </summary>
public sealed class EventConfiguration : IEntityTypeConfiguration<EventAggregate>
{
    public void Configure(EntityTypeBuilder<EventAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("events");
        builder.HasKey(eventEntity => new { eventEntity.Fab, eventEntity.Id, eventEntity.IngestedAt });

        builder.Property(eventEntity => eventEntity.Id)
            .HasColumnName("event_id")
            .HasConversion(id => id.Value, value => EventIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(eventEntity => eventEntity.Fab)
            .HasColumnName("fab_id")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.Source)
            .HasColumnName("source")
            .HasMaxLength(16)
            .HasConversion(source => source.Value, value => Source.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.Device)
            .HasColumnName("device_id")
            .HasMaxLength(DeviceIdentifier.MaximumLength)
            .HasConversion(device => device.Value, value => DeviceIdentifier.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.Kind)
            .HasColumnName("kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(kind => kind.Value, value => Kind.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(occurredAt => occurredAt.Value, value => OccurredAt.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.IngestedAt)
            .HasColumnName("ingested_at")
            .HasConversion(ingestedAt => ingestedAt.Value, value => IngestedAt.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .HasConversion(payload => payload.Value, value => Payload.From(value))
            .IsRequired();

        builder.Property(eventEntity => eventEntity.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.Ignore(eventEntity => eventEntity.PendingEvents);
    }
}
