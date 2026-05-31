using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Layout"/> aggregate (spec 003).
///
/// <para>
/// Revisions are an owned collection mapped to <c>layout_revisions</c>;
/// the aggregate boundary stays inside the chain so the
/// at-most-one-Published invariant lives in a single transaction. A
/// partial unique index in Postgres backs the invariant as a
/// belt-and-braces guard (FR-002).
/// </para>
///
/// <para>
/// FR-006 (name unique across non-archived chains) is enforced by
/// application code in <c>CreateLayoutDraftCommandHandler</c> via the
/// repository's <c>GetByNameAsync</c> lookup. A function-backed partial
/// index on the SQL side is deferred — the application check is
/// authoritative for v1.
/// </para>
/// </summary>
public sealed class LayoutConfiguration : IEntityTypeConfiguration<Layout>
{
    public void Configure(EntityTypeBuilder<Layout> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("layouts");
        builder.HasKey(layout => layout.Id);

        builder.Property(layout => layout.Id)
            .HasColumnName("layout_id")
            .HasConversion(id => id.Value, value => LayoutIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(layout => layout.Name)
            .HasColumnName("name")
            .HasMaxLength(LayoutName.MaximumLength)
            .HasConversion(name => name.Value, value => LayoutName.From(value))
            .IsRequired();

        builder.Property(layout => layout.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(layout => layout.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(layout => layout.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        builder.HasIndex(layout => layout.Name)
            .HasDatabaseName("ix_layouts_name");

        builder.OwnsMany(layout => layout.Revisions, revisions =>
        {
            revisions.ToTable("layout_revisions");
            revisions.WithOwner().HasForeignKey("layout_id");
            revisions.HasKey(revision => revision.Id);

            revisions.Property(revision => revision.Id)
                .HasColumnName("revision_id")
                .HasConversion(id => id.Value, value => LayoutRevisionIdentifier.From(value))
                .ValueGeneratedNever();

            revisions.Property(revision => revision.Number)
                .HasColumnName("revision_number")
                .HasConversion(number => number.Value, value => LayoutRevisionNumber.From(value))
                .IsRequired();

            revisions.Property(revision => revision.State)
                .HasColumnName("state")
                .HasMaxLength(16)
                .HasConversion(state => state.Value, value => LayoutRevisionState.From(value))
                .IsRequired();

            revisions.Property(revision => revision.Camera)
                .HasColumnName("camera_id")
                .HasConversion(camera => camera.Value, value => CameraIdentifier.From(value))
                .IsRequired();

            revisions.Property(revision => revision.Overlay)
                .HasColumnName("overlay_id")
                .HasConversion(
                    overlay => overlay.HasValue ? overlay.Value.Value : (Guid?)null,
                    value => value.HasValue ? OverlayIdentifier.From(value.Value) : (OverlayIdentifier?)null)
                .IsRequired(false);

            revisions.Property(revision => revision.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            revisions.Property(revision => revision.CreatedBy)
                .HasColumnName("created_by")
                .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
                .IsRequired();

            revisions.Property(revision => revision.PublishedAt)
                .HasColumnName("published_at")
                .IsRequired(false);

            revisions.Property(revision => revision.ArchivedAt)
                .HasColumnName("archived_at")
                .IsRequired(false);

            revisions.HasIndex("layout_id", nameof(Revision.Number))
                .HasDatabaseName("ux_layout_revisions_number")
                .IsUnique();

            // Belt-and-braces: at most one Published revision per chain.
            // The aggregate enforces this in-memory; the partial unique
            // index makes a buggy code path fail loudly at COMMIT instead
            // of silently leaving two Published rows.
            revisions.HasIndex("layout_id")
                .HasDatabaseName("ux_layout_revisions_one_published")
                .IsUnique()
                .HasFilter("state = 'Published'");
        });

        builder.Ignore(layout => layout.PendingEvents);
    }
}
