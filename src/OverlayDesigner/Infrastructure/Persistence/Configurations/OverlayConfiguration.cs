using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Overlay"/> aggregate (spec 004).
///
/// <para>
/// Mirrors LayoutComposition's LayoutConfiguration: revisions are an
/// owned collection mapped to <c>overlay_revisions</c>; the partial
/// unique index on <c>state = 'Published'</c> backs the aggregate's
/// at-most-one-Published invariant as a belt-and-braces guard.
/// </para>
///
/// <para>
/// The <see cref="Label"/> value object is flattened across six columns
/// rather than mapped as a separate owned entity — kiosks need to render
/// every Published revision without joins, and Label has no identity of
/// its own.
/// </para>
/// </summary>
public sealed class OverlayConfiguration : IEntityTypeConfiguration<Overlay>
{
    public void Configure(EntityTypeBuilder<Overlay> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("overlays");
        builder.HasKey(overlay => overlay.Id);

        builder.Property(overlay => overlay.Id)
            .HasColumnName("overlay_id")
            .HasConversion(id => id.Value, value => OverlayIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(overlay => overlay.Name)
            .HasColumnName("name")
            .HasMaxLength(OverlayName.MaximumLength)
            .HasConversion(name => name.Value, value => OverlayName.From(value))
            .IsRequired();

        builder.Property(overlay => overlay.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(overlay => overlay.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(overlay => overlay.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(overlay => overlay.Name)
            .HasDatabaseName("ix_overlays_name");

        builder.OwnsMany(overlay => overlay.Revisions, revisions =>
        {
            revisions.ToTable("overlay_revisions");
            revisions.WithOwner().HasForeignKey("overlay_id");
            revisions.HasKey(r => r.Id);

            revisions.Property(r => r.Id)
                .HasColumnName("revision_id")
                .HasConversion(id => id.Value, value => OverlayRevisionIdentifier.From(value))
                .ValueGeneratedNever();

            revisions.Property(r => r.Number)
                .HasColumnName("revision_number")
                .HasConversion(number => number.Value, value => OverlayRevisionNumber.From(value))
                .IsRequired();

            revisions.Property(r => r.State)
                .HasColumnName("state")
                .HasMaxLength(16)
                .HasConversion(state => state.Value, value => OverlayRevisionState.From(value))
                .IsRequired();

            revisions.OwnsOne(r => r.Label, label =>
            {
                label.Property(l => l.Text)
                    .HasColumnName("label_text")
                    .HasMaxLength(Label.MaximumTextLength)
                    .IsRequired();
                label.Property(l => l.NormalizedX).HasColumnName("label_x").IsRequired();
                label.Property(l => l.NormalizedY).HasColumnName("label_y").IsRequired();
                label.Property(l => l.NormalizedWidth).HasColumnName("label_width").IsRequired();
                label.Property(l => l.NormalizedHeight).HasColumnName("label_height").IsRequired();
                label.Property(l => l.FontSizePx).HasColumnName("label_font_size_px").IsRequired();
            });

            revisions.Property(r => r.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            revisions.Property(r => r.CreatedBy)
                .HasColumnName("created_by")
                .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
                .IsRequired();

            revisions.Property(r => r.PublishedAt)
                .HasColumnName("published_at")
                .IsRequired(false);

            revisions.Property(r => r.ArchivedAt)
                .HasColumnName("archived_at")
                .IsRequired(false);

            revisions.HasIndex("overlay_id", nameof(Revision.Number))
                .HasDatabaseName("ux_overlay_revisions_number")
                .IsUnique();

            revisions.HasIndex("overlay_id")
                .HasDatabaseName("ux_overlay_revisions_one_published")
                .IsUnique()
                .HasFilter("state = 'Published'");
        });

        builder.Ignore(overlay => overlay.PendingEvents);
    }
}
