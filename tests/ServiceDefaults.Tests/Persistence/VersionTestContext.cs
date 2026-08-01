using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Tests.Persistence;

/// <summary>
/// Minimal EF model for exercising
/// <see cref="ServiceDefaults.Persistence.AggregateVersionInterceptor"/>.
/// Deliberately mirrors <c>Layout</c>: a root owning a collection of
/// revisions in their own table, each owning a collection of tiles in
/// theirs. That nesting is the case where a child-only change leaves the
/// root row untouched, so a naive implementation silently skips it.
/// </summary>
public sealed class VersionTestContext(DbContextOptions<VersionTestContext> options) : DbContext(options)
{
    public DbSet<TestRoot> Roots => Set<TestRoot>();

    public static VersionTestContext Create() =>
        new(new DbContextOptionsBuilder<VersionTestContext>()
            .UseNpgsql("Host=localhost;Database=version-tests;Username=none;Password=none")
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestRoot>(root =>
        {
            root.ToTable("test_roots");
            root.HasKey(entity => entity.Id);
            root.Property(entity => entity.Version).IsConcurrencyToken();

            root.OwnsMany(entity => entity.Revisions, revision =>
            {
                revision.ToTable("test_revisions");
                revision.WithOwner().HasForeignKey("root_id");
                revision.HasKey(entity => entity.Id);

                revision.OwnsMany(entity => entity.Tiles, tile =>
                {
                    tile.ToTable("test_tiles");
                    tile.WithOwner().HasForeignKey("revision_id");
                    tile.HasKey("revision_id", nameof(TestTile.Row));
                });
            });
        });
    }
}

public sealed class TestRoot : IVersionedAggregate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public List<TestRevision> Revisions { get; set; } = [];
}

public sealed class TestRevision
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public List<TestTile> Tiles { get; set; } = [];
}

public sealed class TestTile
{
    public int Row { get; set; }

    public string CameraName { get; set; } = string.Empty;
}
