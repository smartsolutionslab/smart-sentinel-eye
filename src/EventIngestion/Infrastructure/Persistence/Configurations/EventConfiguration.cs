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
        builder.HasKey(e => new { e.Fab, e.Id, e.IngestedAt });

        builder.Property(e => e.Id)
            .HasColumnName("event_id")
            .HasConversion(id => id.Value, value => EventIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(e => e.Fab)
            .HasColumnName("fab_id")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(f => f.Value, v => FabIdentifier.From(v))
            .IsRequired();

        builder.Property(e => e.Source)
            .HasColumnName("source")
            .HasMaxLength(16)
            .HasConversion(s => s.Value, v => Source.From(v))
            .IsRequired();

        builder.Property(e => e.Device)
            .HasColumnName("device_id")
            .HasMaxLength(DeviceIdentifier.MaximumLength)
            .HasConversion(d => d.Value, v => DeviceIdentifier.From(v))
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(Kind.MaximumLength)
            .HasConversion(k => k.Value, v => Kind.From(v))
            .IsRequired();

        builder.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(o => o.Value, v => OccurredAt.From(v))
            .IsRequired();

        builder.Property(e => e.IngestedAt)
            .HasColumnName("ingested_at")
            .HasConversion(i => i.Value, v => IngestedAt.From(v))
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .HasConversion(p => p.Value, v => Payload.From(v))
            .IsRequired();

        builder.Property(e => e.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.Ignore(e => e.PendingEvents);
    }
}
