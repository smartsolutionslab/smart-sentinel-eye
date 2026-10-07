using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;

/// <summary>
/// Fluent builder for Layout aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario. Returns a Layout whose
/// only revision is a fresh Draft (a single 1×1 tile, mirroring a
/// migrated single-camera layout) so tests typically Publish first.
/// </summary>
public sealed class LayoutBuilder
{
    private FabIdentifier fab = FabIdentifier.From("munich");
    private LayoutName name = LayoutName.From("Line-1-Entrance");
    private CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
    private OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private Option<OverlayIdentifier> overlay = Option<OverlayIdentifier>.None;
    private GridDimensions grid = GridDimensions.Cell;
    private IReadOnlyList<Tile> tiles = null!;
    private IClock clock = new TestClock(
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture));

    public LayoutBuilder WithFab(FabIdentifier fab)
    {
        this.fab = fab;
        return this;
    }

    public LayoutBuilder Named(string name)
    {
        this.name = LayoutName.From(name);
        return this;
    }

    public LayoutBuilder ForCamera(CameraIdentifier camera)
    {
        this.camera = camera;
        return this;
    }

    public LayoutBuilder WithOverlay(OverlayIdentifier overlay)
    {
        this.overlay = Option<OverlayIdentifier>.Some(overlay);
        return this;
    }

    public LayoutBuilder CreatedBy(OperatorIdentifier createdBy)
    {
        this.createdBy = createdBy;
        return this;
    }

    public LayoutBuilder WithGrid(GridDimensions grid)
    {
        this.grid = grid;
        return this;
    }

    public LayoutBuilder WithTiles(IReadOnlyList<Tile> tiles)
    {
        this.tiles = tiles;
        return this;
    }

    public LayoutBuilder At(DateTimeOffset moment)
    {
        clock = new TestClock(moment);
        return this;
    }

    public Domain.Layout.Layout Build()
    {
        IReadOnlyList<Tile> resolvedTiles = tiles
            ?? new[] { new Tile(camera, overlay, GridPosition.From(0, 0)) };
        return Domain.Layout.Layout.CreateDraft(fab, name, grid, resolvedTiles, createdBy, clock);
    }

    public IClock Clock => clock;

    public OperatorIdentifier Operator => createdBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
