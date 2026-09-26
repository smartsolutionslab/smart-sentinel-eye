using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Wall"/> aggregate (spec 258 US1,
/// plan.md §4.1).
///
/// <para>
/// <b><c>Scenes</c> is a Postgres <c>uuid[]</c> column, not an owned
/// table</b> — a deliberate departure from plan.md §4.1's stated preference.
/// <c>OwnsMany</c> requires its owned type to be a reference type (EF Core's
/// <c>TRelatedEntity : class</c> constraint), and <see cref="LayoutIdentifier"/>
/// is a <c>readonly record struct</c> (ADR-0090); <c>layout_revision_tiles</c>
/// can be an owned table because <see cref="Tile"/> is a class. Introducing a
/// class purely to satisfy this constraint, with no other reason to exist,
/// would be exactly the speculative generality constitution §IX warns
/// against. Order survives round-trip (Postgres arrays are ordered; the
/// converter below reads and writes by index), which is all FR-002 needs.
/// </para>
///
/// <para>
/// There is deliberately no <c>archived_at</c> column and no partial filter
/// on the name index yet: archiving a wall is a follow-up feature (spec 258
/// §3), and adding either now would be speculative generality.
/// </para>
/// </summary>
public sealed class WallConfiguration : IEntityTypeConfiguration<Wall>
{
    public void Configure(EntityTypeBuilder<Wall> builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.ToTable("walls");
        builder.HasKey(wall => wall.Id);

        builder.Property(wall => wall.Id)
            .HasColumnName("wall_id")
            .HasConversion(id => id.Value, value => WallIdentifier.From(value))
            .ValueGeneratedNever();

        builder.Property(wall => wall.Fab)
            .HasColumnName("fab")
            .HasMaxLength(FabIdentifier.MaximumLength)
            .HasConversion(fab => fab.Value, value => FabIdentifier.From(value))
            .IsRequired();

        builder.Property(wall => wall.Name)
            .HasColumnName("name")
            .HasMaxLength(WallName.MaximumLength)
            .HasConversion(name => name.Value, value => WallName.From(value))
            .IsRequired();

        builder.Property(wall => wall.Showing)
            .HasColumnName("showing_layout_id")
            .HasConversion(showing => showing.Value, value => LayoutIdentifier.From(value))
            .IsRequired();

        builder.Property(wall => wall.SceneVersion)
            .HasColumnName("scene_version")
            .HasConversion(version => version.Value, value => SceneVersion.From(value))
            .IsRequired();

        builder.Property(wall => wall.ShowingSince)
            .HasColumnName("showing_since")
            .HasConversion(since => since.Value, value => ShowingSince.From(value))
            .IsRequired();

        builder.OwnsOne(wall => wall.Creation, creation =>
        {
            creation.Property(value => value.At)
                .HasColumnName("created_at")
                .HasConversion(at => at.Value, value => CreatedAt.From(value))
                .IsRequired();

            creation.Property(value => value.By)
                .HasColumnName("created_by")
                .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
                .IsRequired();
        });
        builder.Navigation(wall => wall.Creation).IsRequired();

        builder.Property(wall => wall.Version)
            .HasColumnName("version")
            .HasConversion(version => version.Value, value => AggregateVersion.From(value))
            .IsConcurrencyToken()
            .IsRequired();

        // PD-2: unique per fab among live walls. Case-insensitivity
        // (lower(name)) is expressed as raw SQL in the migration — EF's
        // fluent HasIndex cannot describe a Postgres expression index.
        builder.HasIndex(wall => new { wall.Fab, wall.Name })
            .HasDatabaseName("ux_walls_fab_name_ci")
            .IsUnique();

        ValueComparer<IReadOnlyList<LayoutIdentifier>> scenesComparer = new(
            (left, right) => left!.SequenceEqual(right!),
            scenes => scenes.Aggregate(0, (hash, scene) => HashCode.Combine(hash, scene.Value)),
            scenes => scenes.ToList());

        builder.Property(wall => wall.Scenes)
            .HasColumnName("scenes")
            .HasConversion(
                scenes => scenes.Select(scene => scene.Value).ToArray(),
                value => value.Select(LayoutIdentifier.From).ToList(),
                scenesComparer)
            .IsRequired();

        builder.Ignore(wall => wall.PendingEvents);
    }
}
