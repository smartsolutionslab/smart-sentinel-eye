using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class EditDraftRevisionCommandHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static Tile TileAt(int row, int col, OverlayIdentifier? overlay = null) =>
        new(
            CameraIdentifier.From(Guid.CreateVersion7()),
            overlay.HasValue ? Option<OverlayIdentifier>.Some(overlay.Value) : Option<OverlayIdentifier>.None,
            GridPosition.From(row, col));

    [Fact]
    public async Task Editing_a_Draft_replaces_its_grid_and_tile_set()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.One, GridDimensions.Default, [TileAt(0, 0), TileAt(1, 1)], 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Revision revision = layout.Revisions.Single();
        revision.Grid.ShouldBe(GridDimensions.Default);
        revision.Tiles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), new FakeClock(FixedMoment), NullLogger<EditDraftRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                LayoutIdentifier.New(), LayoutRevisionNumber.One, GridDimensions.Cell, [TileAt(0, 0)], 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.From(42), GridDimensions.Cell, [TileAt(0, 0)], 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutRevisionNotFound>();
    }

    [Fact]
    public async Task Editing_a_tile_with_an_overlay_binds_it()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.One, GridDimensions.Cell, [TileAt(0, 0, overlay)], 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        layout.Revisions.Single().Tiles.Single().Overlay.Value.ShouldBe(overlay);
    }

    [Fact]
    public async Task An_empty_tile_set_returns_LAYOUT_GRID_EMPTY()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.One, GridDimensions.Cell, Array.Empty<Tile>(), 0),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.GridEmpty>();
    }

    [Fact]
    public async Task A_duplicate_tile_position_returns_LAYOUT_TILE_POSITION_DUPLICATE()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.One, GridDimensions.Default, [TileAt(0, 0), TileAt(0, 0)], 0),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.TilePositionDuplicate>();
    }

    [Fact]
    public async Task Editing_a_Published_revision_returns_NotADraft()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        EditDraftRevisionCommandHandler handler = new(
            layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id, LayoutRevisionNumber.One, GridDimensions.Cell, [TileAt(0, 0)], 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.NotADraft>();
    }
}
