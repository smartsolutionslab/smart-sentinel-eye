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
        Ensure.That(builder).IsNotNull();

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

        // Nullable, unlike every sibling context's fab column: cameras live in
        // another database, so no migration here can derive a pre-existing
        // stream's fab. Those rows acquire it at runtime instead and are
        // visible to nobody until they do (FR-009). A follow-up migration
        // tightens this to NOT NULL once no unattributed rows remain.
        builder.Property(stream => stream.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            // `fab!` is safe: EF does not invoke a converter for a null value,
            // so the lambda only ever sees an attributed stream.
            .HasConversion(fab => fab!.Value, value => FabIdentifier.From(value))
            .IsRequired(false);

        builder.Property(stream => stream.Path)
            .HasColumnName("mediamtx_path")
            .HasMaxLength(80)
            .HasConversion(path => path.Value, value => MediaMtxPath.From(value))
            .IsRequired();

        builder.Property(stream => stream.SourceUrl)
            .HasColumnName("source_url")
            .HasMaxLength(StreamSourceUrl.MaximumLength)
            .HasConversion(url => url.Value, value => StreamSourceUrl.From(value))
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
            .HasMaxLength(StreamError.MaximumLength)
            .HasConversion(error => error.Value, value => StreamError.From(value))
            .IsRequired(false);

        builder.Property(stream => stream.ProvisionedAt)
            .HasColumnName("provisioned_at")
            .IsRequired();

        builder.Property(stream => stream.ProvisionedBy)
            .HasColumnName("provisioned_by")
            .HasConversion(operatorIdentifier => operatorIdentifier.Value, value => OperatorIdentifier.From(value))
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

        // Plain, not unique. Rules, variables and cameras make (fab, name)
        // unique; a stream has no name — it is keyed by its camera, which is
        // already globally unique — so a composite index would be redundant.
        // This one supports the listing filter, the only query the column
        // takes part in.
        builder.HasIndex(stream => stream.Fab)
            .HasDatabaseName("ix_streams_fab");

        builder.Ignore(stream => stream.PendingEvents);
    }
}
