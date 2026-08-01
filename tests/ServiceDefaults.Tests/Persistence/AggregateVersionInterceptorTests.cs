using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Tests.Persistence;

/// <summary>
/// Covers ADR-0113's Layer 2. These assert on the change tracker rather
/// than on generated SQL; that the resulting UPDATE actually loses the
/// race is proved by the integration test against real Postgres.
/// </summary>
public class AggregateVersionInterceptorTests
{
    [Fact]
    public void A_modified_root_has_its_version_incremented_by_one()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 7);

        root.Name = "renamed";
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(8);
    }

    [Fact]
    public void An_added_root_is_not_bumped()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = NewRoot();

        context.Roots.Add(root);
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(0);
    }

    [Fact]
    public void An_untouched_root_is_neither_bumped_nor_marked_modified()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 3);

        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(3);
        context.Entry(root).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public void The_bump_leaves_the_original_value_so_the_where_clause_still_targets_the_loaded_row()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 12);

        root.Name = "renamed";
        AggregateVersionInterceptor.BumpVersions(context);

        PropertyEntry version = VersionOf(context, root);
        version.OriginalValue.ShouldBe(12);
        version.CurrentValue.ShouldBe(13);
    }

    // The three cases below are the reason this interceptor exists. When only
    // an owned child row changes, EF issues no UPDATE against the root table,
    // so the root's concurrency token is never in a WHERE clause at all.

    [Fact]
    public void A_modified_owned_child_bumps_the_root_and_promotes_it_to_modified()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 4);

        root.Revisions[0].Label = "edited";
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(5);
        context.Entry(root).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public void An_added_owned_child_bumps_the_root()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 4);

        root.Revisions.Add(new TestRevision { Id = Guid.CreateVersion7(), Label = "second" });
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(5);
    }

    [Fact]
    public void A_removed_owned_child_bumps_the_root()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 4);

        root.Revisions.RemoveAt(0);
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(5);
    }

    [Fact]
    public void A_change_nested_two_levels_deep_bumps_the_root()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot root = AttachedRoot(context, version: 4);

        root.Revisions[0].Tiles[0].CameraName = "camera-2";
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, root).CurrentValue.ShouldBe(5);
    }

    [Fact]
    public void A_second_root_is_untouched_when_only_the_first_one_changes()
    {
        using VersionTestContext context = VersionTestContext.Create();
        TestRoot changed = AttachedRoot(context, version: 4);
        TestRoot untouched = AttachedRoot(context, version: 9);

        changed.Revisions[0].Label = "edited";
        AggregateVersionInterceptor.BumpVersions(context);

        VersionOf(context, changed).CurrentValue.ShouldBe(5);
        VersionOf(context, untouched).CurrentValue.ShouldBe(9);
    }

    private static TestRoot AttachedRoot(VersionTestContext context, int version)
    {
        TestRoot root = NewRoot();
        root.Version = version;
        context.Attach(root);

        return root;
    }

    private static TestRoot NewRoot() => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "original",
        Revisions =
        [
            new TestRevision
            {
                Id = Guid.CreateVersion7(),
                Label = "first",
                Tiles = [new TestTile { Row = 0, CameraName = "camera-1" }],
            },
        ],
    };

    private static PropertyEntry VersionOf(VersionTestContext context, TestRoot root) =>
        context.Entry(root).Property(nameof(IVersionedAggregate.Version));
}
