using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class CreateLayoutDraftCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static Tile TileAt(int row, int col, OverlayIdentifier? overlay = null) =>
        new(
            CameraIdentifier.From(Guid.CreateVersion7()),
            overlay.HasValue ? Option<OverlayIdentifier>.Some(overlay.Value) : Option<OverlayIdentifier>.None,
            GridPosition.From(row, col));

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static CreateLayoutDraftCommand Command(
        string name, GridDimensions grid, IReadOnlyList<Tile> tiles) =>
        Command(Munich, name, grid, tiles);

    private static CreateLayoutDraftCommand Command(
        FabIdentifier fab, string name, GridDimensions grid, IReadOnlyList<Tile> tiles) =>
        new(fab, LayoutName.From(name), grid, tiles, OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task First_creation_with_a_unique_name_returns_a_new_LayoutIdentifier()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Cell, [TileAt(0, 0)]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layouts.Layouts.Count.ShouldBe(1);
        Layout created = layouts.Layouts[0];
        created.Id.ShouldBe(result.Value);
        created.Name.Value.ShouldBe("Line-1");
        created.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public async Task Creating_a_2x2_wall_carries_every_tile_onto_the_initial_Draft()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Default, [TileAt(0, 0), TileAt(0, 1), TileAt(1, 0), TileAt(1, 1)]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Revision revision = layouts.Layouts[0].Revisions.Single();
        revision.Grid.ShouldBe(GridDimensions.Default);
        revision.Tiles.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Creating_with_an_overlay_carries_it_onto_the_initial_Draft_tile()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Cell, [TileAt(0, 0, overlay)]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layouts.Layouts[0].Revisions.Single().Tiles.Single().Overlay.Value.ShouldBe(overlay);
    }

    [Fact]
    public async Task An_empty_tile_set_returns_LAYOUT_GRID_EMPTY()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Cell, Array.Empty<Tile>()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateLayoutDraftError.GridEmpty>();
        result.Error.Code.ShouldBe("LAYOUT_GRID_EMPTY");
        layouts.Layouts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_duplicate_tile_position_returns_LAYOUT_TILE_POSITION_DUPLICATE()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Default, [TileAt(0, 0), TileAt(0, 0)]),
            CancellationToken.None);

        result.Error.ShouldBeOfType<CreateLayoutDraftError.TilePositionDuplicate>();
    }

    [Fact]
    public async Task An_out_of_bounds_tile_returns_LAYOUT_TILE_OUT_OF_BOUNDS()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Cell, [TileAt(0, 1)]),
            CancellationToken.None);

        result.Error.ShouldBeOfType<CreateLayoutDraftError.TileOutOfBounds>();
    }

    [Fact]
    public async Task A_name_collision_with_a_non_archived_chain_returns_LayoutNameTaken()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout existing = new LayoutBuilder().Named("Line-1").At(FixedMoment).Build();
        layouts.Add(existing);

        CreateLayoutDraftCommandHandler handler = new(layouts, clock, NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command("Line-1", GridDimensions.Cell, [TileAt(0, 0)]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateLayoutDraftError.LayoutNameTaken>();
        layouts.Layouts.Count.ShouldBe(1);
    }

    /// <summary>
    /// FR-019, and the half that is a leak rather than an inconvenience. A
    /// global name check answers LAYOUT_NAME_TAKEN for a layout the caller
    /// cannot see — the same enumeration oracle FR-006 closes on the read
    /// path, reappearing on the write path.
    /// </summary>
    [Fact]
    public async Task The_same_name_is_accepted_in_a_second_fab()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        layouts.Add(new LayoutBuilder().WithFab(Munich).Named("Line-1").At(FixedMoment).Build());

        CreateLayoutDraftCommandHandler handler = new(layouts, clock, NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command(Dresden, "Line-1", GridDimensions.Cell, [TileAt(0, 0)]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layouts.Layouts.Count.ShouldBe(2);
    }

    /// <summary>
    /// The other half: within one fab the name is still taken, so FR-019
    /// widened the key rather than dropping the constraint.
    /// </summary>
    [Fact]
    public async Task The_same_name_is_still_refused_within_one_fab()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        layouts.Add(new LayoutBuilder().WithFab(Dresden).Named("Line-1").At(FixedMoment).Build());

        CreateLayoutDraftCommandHandler handler = new(layouts, clock, NullLogger<CreateLayoutDraftCommandHandler>.Instance);
        Result<LayoutIdentifier, CreateLayoutDraftError> result = await handler.HandleAsync(
            Command(Dresden, "Line-1", GridDimensions.Cell, [TileAt(0, 0)]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateLayoutDraftError.LayoutNameTaken>();
        layouts.Layouts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_created_layout_carries_the_resolved_fab()
    {
        InMemoryLayoutRepository layouts = new();
        CreateLayoutDraftCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<CreateLayoutDraftCommandHandler>.Instance);

        await handler.HandleAsync(
            Command(Dresden, "Line-9", GridDimensions.Cell, [TileAt(0, 0)]),
            CancellationToken.None);

        // dresden, not munich: everything else defaults to munich, so a fab
        // dropped on the floor would still pass a munich assertion.
        layouts.Layouts.Single().Fab.ShouldBe(Dresden);
    }
}
