using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the Stream aggregate. Value objects are flattened to
/// plain columns. LastSuccessAt and LastError are nullable types per the
/// ADR-0048 carve-out documented in Stream.cs. Unique index on camera_id
/// enforces "one stream per camera".
/// </summary>
public sealed class StreamConfiguration : IEntityTypeConfiguration<Domain.Stream.Stream>
{
    public void Configure(EntityTypeBuilder<Domain.Stream.Stream> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("streams");

        builder.HasKey(stream => stream.Id);

        builder.Property(stream => stream.Id)
            .HasColumnName("stream_id")
            .HasConversion(id => id.Value, value => StreamIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(stream => stream.Camera)
            .HasColumnName("camera_id")
            .HasConversion(camera => camera.Value, value => CameraIdentifier.From(value))
            .IsRequired();

        builder.Property(stream => stream.Path)
            .HasColumnName("mediamtx_path")
            .HasMaxLength(80)
            .HasConversion(path => path.Value, value => MediaMtxPath.From(value))
            .IsRequired();

        builder.Property(stream => stream.State)
            .HasColumnName("state")
            .HasMaxLength(16)
            .HasConversion(state => state.Value, value => StreamState.From(value))
            .IsRequired();

        builder.Property(stream => stream.TranscodeMode)
            .HasColumnName("transcode_mode")
            .HasMaxLength(16)
            .HasConversion(mode => mode.Value, value => TranscodeMode.From(value))
            .IsRequired();

        builder.Property(stream => stream.LastSuccessAt)
            .HasColumnName("last_success_at")
            .IsRequired(false);

        builder.Property(stream => stream.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(stream => stream.ProvisionedAt)
            .HasColumnName("provisioned_at")
            .IsRequired();

        builder.Property(stream => stream.ProvisionedBy)
            .HasColumnName("provisioned_by")
            .HasConversion(op => op.Value, value => OperatorIdentifier.From(value))
            .IsRequired();

        builder.Property(stream => stream.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        // One stream per camera (FR-011 idempotency enforced at the DB layer too).
        builder.HasIndex(stream => stream.Camera)
            .HasDatabaseName("ux_streams_camera_id")
            .IsUnique();

        builder.HasIndex(stream => stream.Path)
            .HasDatabaseName("ux_streams_mediamtx_path")
            .IsUnique();

        builder.Ignore(stream => stream.PendingEvents);
    }
}
