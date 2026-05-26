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
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cameras");

        builder.HasKey(camera => camera.Id);

        builder.Property(camera => camera.Id)
            .HasColumnName("camera_id")
            .HasConversion(id => id.Value, value => CameraIdentifier.From(value))
            .ValueGeneratedNever();

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
            .HasConversion(op => op.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(camera => camera.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // Case-insensitive uniqueness on Name per spec 001-register-camera marker 2.
        // Postgres-specific: a unique btree index on the name column.
        builder.HasIndex(camera => camera.Name)
            .HasDatabaseName("ux_cameras_name_lower")
            .IsUnique()
            .HasMethod("btree");

        builder.Ignore(camera => camera.PendingEvents);
    }
}
