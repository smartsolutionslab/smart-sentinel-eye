using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="SourceMode"/> aggregate (spec 269
/// plan.md §4). The natural key <c>(fab, source)</c> is enforced twice
/// (spec 086): the application-level lookup, and the total unique index
/// below — no lifecycle state, so unlike
/// <c>ux_registered_event_types_fab_kind</c> this index has no filter.
/// </summary>
public sealed class SourceModeConfiguration : IEntityTypeConfiguration<SourceMode>
{
    public void Configure(EntityTypeBuilder<SourceMode> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("source_modes");
        builder.HasKey(sourceMode => sourceMode.Id);

        builder.Property(sourceMode => sourceMode.Id)
            .HasColumnName("source_mode_id")
            .HasConversion(id => id.Value, value => SourceModeIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(sourceMode => sourceMode.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(sourceMode => sourceMode.Source)
            .HasColumnName("source")
            .HasMaxLength(16)
            .HasConversion(source => source.Value, value => Source.From(value))
            .IsRequired();

        builder.Property(sourceMode => sourceMode.Mode)
            .HasColumnName("mode")
            .HasMaxLength(16)
            .HasConversion(mode => mode.Value, value => EventTypeMode.From(value))
            .IsRequired();

        // Owned reference onto declared_at + declared_by. Navigation(...)
        // .IsRequired() keeps them NOT NULL; without it the model silently
        // diverges from the schema (VariableConfiguration.cs:86,98; #2022).
        builder.OwnsOne(sourceMode => sourceMode.Declaration, declaration =>
        {
            declaration.Property(value => value.DeclaredAt)
                .HasColumnName("declared_at")
                .HasConversion(at => at.Value, value => DeclaredAt.From(value))
                .IsRequired();

            declaration.Property(value => value.DeclaredBy)
                .HasColumnName("declared_by")
                .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                .IsRequired();
        });
        builder.Navigation(sourceMode => sourceMode.Declaration).IsRequired();

        builder.Property(sourceMode => sourceMode.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        // Total: no lifecycle state to filter on (plan.md §4). Leads with
        // fab, so it also serves the ingest lookup's fab IN (…) — no second
        // index on fab is added.
        builder.HasIndex(sourceMode => new { sourceMode.Fab, sourceMode.Source })
            .HasDatabaseName("ux_source_modes_fab_source")
            .IsUnique();

        builder.Ignore(sourceMode => sourceMode.PendingEvents);
    }
}
