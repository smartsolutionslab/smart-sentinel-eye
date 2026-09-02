using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the Camera aggregate. Value objects are flattened to
/// plain columns; case-insensitive uniqueness on Name is enforced by a unique
/// index over a stored generated column holding <c>upper(name)</c>
/// (Postgres-specific).
/// </summary>
public sealed class CameraConfiguration : IEntityTypeConfiguration<Camera>
{
    /// <summary>
    /// Shadow property over the generated <c>name_normalized</c> column. Named
    /// here rather than spelled at each call site because
    /// <see cref="CameraRepository"/> has to query it by the same string, and a
    /// typo there fails as "no rows", not as an error (#1434).
    /// </summary>
    public const string NormalizedNameProperty = "NameNormalized";

    private const string CameraFabProperty = nameof(Camera.Fab);

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

        // The comparer is not optional here, and its absence is invisible until
        // something renames a camera by letter case alone (spec 033).
        //
        // Without it EF derives a comparer from CameraName, whose Equals
        // compares NormalizedValue — correct for uniqueness, and wrong for
        // change detection. `Line-3` and `line-3` compare equal, so EF sees no
        // change and issues no UPDATE: the aggregate raises its event, the
        // handler saves, the endpoint answers 204, and the stored name never
        // moves. Every unit test passes, because the in-memory double has no
        // change tracker.
        builder.Property(camera => camera.Name)
            .HasColumnName("name")
            .HasMaxLength(CameraName.MaximumLength)
            .HasConversion(
                name => name.Value,
                value => CameraName.From(value),
                new ValueComparer<CameraName>(
                    (left, right) =>
                        left == null
                            ? right == null
                            : right != null && string.Equals(left.Value, right.Value, StringComparison.Ordinal),
                    name => name == null ? 0 : name.Value.GetHashCode(StringComparison.Ordinal),
                    name => CameraName.From(name.Value)))
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

        // Owned reference onto the two columns the pair occupied. The
        // Navigation(...).IsRequired() line keeps them NOT NULL; without it EF
        // treats the reference as optional and the model silently diverges
        // from the schema (#2022).
        builder.OwnsOne(camera => camera.Registration, registration =>
        {
            registration.Property(value => value.At)
                .HasColumnName("registered_at")
                .HasConversion(at => at.Value, value => RegisteredAt.From(value))
                .IsRequired();

            registration.Property(value => value.By)
                .HasColumnName("registered_by")
                .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                .IsRequired();
        });
        builder.Navigation(camera => camera.Registration).IsRequired();

        builder.Property(camera => camera.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        // #1434. The normalised name, computed by Postgres rather than written
        // by us: a stored generated column cannot drift from `name`, whereas a
        // column the application maintains is one forgotten assignment away
        // from the defect this replaces.
        //
        // `upper`, not `lower`, to match CameraName.NormalizedValue — the
        // domain and the database must agree on what "the same name" means, and
        // the two normalisations differ for some Unicode (ß, Turkish i).
        builder.Property<string>(NormalizedNameProperty)
            .HasColumnName("name_normalized")
            .HasMaxLength(CameraName.MaximumLength)
            .HasComputedColumnSql("upper(name)", stored: true);

        // Case-insensitive uniqueness on Name *within a fab* (spec 015 FR-002,
        // spec 001 marker 2).
        //
        // #1434: this index was `(fab, name)` and the comment above it claimed
        // case-insensitivity it never had — a plain btree on the raw column,
        // which stores the original casing. `Cam-Entrance` and `cam-entrance`
        // both registered happily. It indexes the generated column now, so the
        // claim and the schema finally say the same thing.
        //
        // The partial filter carries over from spec 015: a decommissioned
        // camera releases its name, as rules and variables do.
        builder.HasIndex(CameraFabProperty, NormalizedNameProperty)
            .HasDatabaseName("ux_cameras_fab_name_normalized_active")
            .IsUnique()
            .HasMethod("btree")
            .HasFilter("status <> 'Decommissioned'");

        builder.Ignore(camera => camera.PendingEvents);
    }
}
