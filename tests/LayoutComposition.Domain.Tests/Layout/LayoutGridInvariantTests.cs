using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Issue #2187 / spec 138: <c>Layout.ValidateGrid</c> has zero callers inside
/// the aggregate today, so <c>CreateDraft</c> and <c>EditDraft</c> silently
/// accept an empty, oversized, out-of-bounds, or duplicate-position tile set.
/// These facts assert the aggregate refuses each violation itself, once
/// <c>Layout.RequireValidGrid</c> is wired into both write paths (plan §3).
///
/// <para>
/// Every refusal is currently RED: the aggregate returns/mutates successfully
/// today, so <c>Should.Throw&lt;InvalidOperationException&gt;()</c> fails with
/// "expected an exception to be thrown, but none was".
/// </para>
/// </summary>
public class LayoutGridInvariantTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static Tile TileAt(CameraIdentifier camera, int row, int col) =>
        new(camera, Option<OverlayIdentifier>.None, GridPosition.From(row, col));

    private static Tile TileAt(int row, int col) =>
        TileAt(CameraIdentifier.From(Guid.CreateVersion7()), row, col);

    // ---- CreateDraft refusals -------------------------------------------

    [Fact]
    public void CreateDraft_refuses_an_empty_tile_set()
    {
        Should.Throw<InvalidOperationException>(() => Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            LayoutName.From("Line-1"),
            GridDimensions.Cell,
            Array.Empty<Tile>(),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    [Fact]
    public void CreateDraft_refuses_two_tiles_at_the_same_position()
    {
        IReadOnlyList<Tile> tiles = [TileAt(0, 0), TileAt(0, 0)];

        Should.Throw<InvalidOperationException>(() => Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            LayoutName.From("Line-1"),
            GridDimensions.Default,
            tiles,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    [Fact]
    public void CreateDraft_refuses_an_out_of_bounds_tile()
    {
        IReadOnlyList<Tile> tiles = [TileAt(0, 1)];

        Should.Throw<InvalidOperationException>(() => Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            LayoutName.From("Line-1"),
            GridDimensions.Cell,
            tiles,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    [Fact]
    public void CreateDraft_refuses_an_oversized_grid()
    {
        IReadOnlyList<Tile> tiles = [TileAt(0, 0)];

        // Direct constructor, not GridDimensions.From(3, 3): From's own
        // Ensure.That guard already rejects > MaxCells, which would make this
        // pass for a reason that has nothing to do with the aggregate
        // (LayoutTests.cs:305-311 uses the same direct-constructor trick).
        Should.Throw<InvalidOperationException>(() => Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            LayoutName.From("Line-1"),
            new GridDimensions(3, 3),
            tiles,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    // ---- CreateDraft still accepts valid input (green from the start) ---

    [Fact]
    public void CreateDraft_still_accepts_a_full_2x2_wall()
    {
        IReadOnlyList<Tile> tiles =
            [TileAt(0, 0), TileAt(0, 1), TileAt(1, 0), TileAt(1, 1)];

        Domain.Layout.Layout layout = Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            LayoutName.From("Line-1"),
            GridDimensions.Default,
            tiles,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment));

        layout.Revisions.Single().Tiles.Count.ShouldBe(4);
    }

    // ---- EditDraft refusals — each also asserts nothing half-applied ----

    [Fact]
    public void EditDraft_refuses_an_empty_tile_set_and_leaves_the_revision_unchanged()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        Should.Throw<InvalidOperationException>(() => layout.EditDraft(
            LayoutRevisionNumber.One, GridDimensions.Cell, Array.Empty<Tile>(), clock));

        Revision only = layout.Revisions.Single();
        only.Grid.ShouldBe(GridDimensions.Cell);
        only.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
    }

    [Fact]
    public void EditDraft_refuses_two_tiles_at_the_same_position_and_leaves_the_revision_unchanged()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        IReadOnlyList<Tile> tiles = [TileAt(0, 0), TileAt(0, 0)];

        Should.Throw<InvalidOperationException>(() => layout.EditDraft(
            LayoutRevisionNumber.One, GridDimensions.Default, tiles, clock));

        Revision only = layout.Revisions.Single();
        only.Grid.ShouldBe(GridDimensions.Cell);
        only.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
    }

    [Fact]
    public void EditDraft_refuses_an_out_of_bounds_tile_and_leaves_the_revision_unchanged()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        IReadOnlyList<Tile> tiles = [TileAt(0, 1)];

        Should.Throw<InvalidOperationException>(() => layout.EditDraft(
            LayoutRevisionNumber.One, GridDimensions.Cell, tiles, clock));

        Revision only = layout.Revisions.Single();
        only.Grid.ShouldBe(GridDimensions.Cell);
        only.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
    }

    [Fact]
    public void EditDraft_refuses_an_oversized_grid_and_leaves_the_revision_unchanged()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        IReadOnlyList<Tile> tiles = [TileAt(0, 0)];

        Should.Throw<InvalidOperationException>(() => layout.EditDraft(
            LayoutRevisionNumber.One, new GridDimensions(3, 3), tiles, clock));

        Revision only = layout.Revisions.Single();
        only.Grid.ShouldBe(GridDimensions.Cell);
        only.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
    }

    // ---- EditDraft still accepts valid input (green from the start) -----

    [Fact]
    public void EditDraft_still_accepts_two_in_bounds_tiles()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        IReadOnlyList<Tile> tiles = [TileAt(0, 0), TileAt(1, 1)];

        layout.EditDraft(LayoutRevisionNumber.One, GridDimensions.Default, tiles, clock);

        layout.Revisions.Single().Tiles.Count.ShouldBe(2);
    }

    // ---- Ordering: Ensure.That guards still win over the grid check -----

    [Fact]
    public void CreateDraft_with_a_null_name_and_an_invalid_grid_still_throws_ArgumentNullException()
    {
        // Both faults are present (null name, empty tile set). The Ensure.That
        // guards run first regardless, so the caller sees the argument fault,
        // not the grid violation. This already holds today (ValidateGrid has
        // no caller yet) and must keep holding once RequireValidGrid is wired
        // in after the guards (plan §3).
        Should.Throw<ArgumentNullException>(() => Domain.Layout.Layout.CreateDraft(
            FabIdentifier.From("munich"),
            name: null!,
            GridDimensions.Cell,
            Array.Empty<Tile>(),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    // ---- Ordering: the grid check precedes RequireRevision / ReplaceTiles ----

    [Fact]
    public void EditDraft_refuses_an_invalid_grid_before_reporting_a_missing_revision_number()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        // Revision 99 does not exist AND the tile set is empty. Today,
        // RequireRevision runs first and throws "has no revision 99" — the
        // grid is never inspected. Once RequireValidGrid runs before
        // RequireRevision (plan §3), the failure names the grid violation
        // instead, because a bad argument is a bad argument regardless of
        // which revision it targets.
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() =>
            layout.EditDraft(LayoutRevisionNumber.From(99), GridDimensions.Cell, Array.Empty<Tile>(), clock));

        exception.Message.ShouldContain("Empty");
        exception.Message.ShouldNotContain("has no revision");
    }

    [Fact]
    public void EditDraft_on_a_Published_revision_with_an_invalid_grid_fails_on_the_grid_first()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        // Today: RequireRevision finds revision 1, ReplaceTiles' Draft-state
        // guard fires ("only Draft revisions are editable") because the
        // revision is Published. Once the grid check precedes RequireRevision
        // (plan §3), the empty tile set is refused before the state is ever
        // consulted — the argument is bad regardless of which revision, or
        // which state, it targets.
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() =>
            layout.EditDraft(LayoutRevisionNumber.One, GridDimensions.Cell, Array.Empty<Tile>(), clock));

        exception.Message.ShouldContain("Empty");
        exception.Message.ShouldNotContain("Draft revisions are editable");
    }
}
