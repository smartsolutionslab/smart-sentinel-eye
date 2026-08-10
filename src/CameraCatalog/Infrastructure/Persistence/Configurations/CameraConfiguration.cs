using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the Camera aggregate. Value objects are flattened to
/// plain columns; case-insensitive uniqueness on Name is enforced via a
/// computed unique index on LOWER(name) (Postgres-specific).
/// </summary>
public sealed class CameraConfiguration : IEntityTypeConfiguration<Camera>
{
    public void Configure(EntityTypeBuilder<Camera> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("cameras");

        builder.HasKey(camera => camera.Id);

        builder.Property(camera => camera.Id)
            .HasColumnName("camera_id")
            .HasConversion(id => id.Value, value => CameraIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(camera => camera.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(camera => camera.Name)
            .HasColumnName("name")
            .HasMaxLength(CameraName.MaximumLength)
            .HasConversion(name => name.Value, value => CameraName.From(value))
            .IsRequired();

        builder.Property(camera => camera.Url)
            .HasColumnName("rtsp_url")
            .HasMaxLength(RtspUrl.MaximumLength)
            .HasConversion(url => url.Value, value => RtspUrl.From(value))
            .IsRequired();

        builder.Property(camera => camera.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion(status => status.Value, value => CameraStatus.From(value))
            .IsRequired();

        builder.Property(camera => camera.RegisteredAt)
            .HasColumnName("registered_at")
            .IsRequired();

        builder.Property(camera => camera.RegisteredBy)
            .HasColumnName("registered_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(camera => camera.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Case-insensitive uniqueness on Name *within a fab* (spec 015 FR-002).
        // Postgres-specific: a unique btree index.
        //
        // The partial filter is NEW behaviour, not a carry-over: the shipped
        // index had none, so a decommissioned camera held its name forever.
        // Rules and variables both release theirs, and adopting the filter was
        // decided at spec 015's Phase 2 gate (research.md §3). It is safe
        // against existing data by construction — a partial unique index is
        // strictly weaker than the unfiltered one it replaces.
        //
        // The case-insensitivity is spec 001 marker 2 and must survive the
        // swap; T015 tests it, because it is exactly what a hand-corrected
        // migration drops silently.
        builder.HasIndex(camera => new { camera.Fab, camera.Name })
            .HasDatabaseName("ux_cameras_fab_name_active")
            .IsUnique()
            .HasMethod("btree")
            .HasFilter("status <> 'Decommissioned'");

        builder.Ignore(camera => camera.PendingEvents);
    }
}
