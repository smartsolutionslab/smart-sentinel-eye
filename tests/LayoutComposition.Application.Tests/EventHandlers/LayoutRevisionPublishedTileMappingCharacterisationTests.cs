using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins the tile projection onto
/// <see cref="LayoutTileV2"/> at the points the existing handler tests leave
/// unchecked. They assert row and column only for a tile at (0,0), and a span
/// only for a square one, so a swapped row/column or rows/cols pair in an
/// extracted projection passes them. Also pins tile order, the published
/// timestamp, the lean notification and publish-before-broadcast.
/// </summary>
public class LayoutRevisionPublishedTileMappingCharacterisationTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private sealed class BroadcastCountingBus(FakeLayoutLifecycleBroadcaster broadcaster) : IEventBus
    {
        public List<object> Published { get; } = [];

        public int BroadcastsBeforeFirstPublish { get; private set; } = -1;

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            BroadcastsBeforeFirstPublish = broadcaster.Published.Count;
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private static LayoutRevisionPublishedDomainEvent EventWith(
        LayoutIdentifier layout,
        IReadOnlyList<Tile> tiles,
        OperatorIdentifier by) =>
        new(
            Munich,
            layout,
            LayoutRevisionNumber.From(3),
            LayoutName.From("Hero-Wall"),
            GridDimensions.From(2, 4),
            tiles,
            FixedMoment,
            by);

    [Fact]
    public async Task A_tile_off_the_origin_with_a_non_square_span_keeps_row_col_and_spans_in_their_own_slots()
    {
        FakeEventBus bus = new();
        LayoutRevisionPublishedDomainEventHandler handler = new(bus, new FakeLayoutLifecycleBroadcaster());
        var camera = CameraIdentifier.From(Guid.CreateVersion7());
        IReadOnlyList<Tile> tiles =
        [
            new Tile(camera, Option<OverlayIdentifier>.None, GridPosition.From(1, 3), TileSpan.From(2, 1)),
        ];

        await handler.Handle(
            EventWith(LayoutIdentifier.New(), tiles, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        var v2 = bus.Published.ShouldHaveSingleItem().ShouldBeOfType<LayoutRevisionPublishedV2>();
        var tile = v2.Tiles.ShouldHaveSingleItem();
        tile.Camera.ShouldBe(camera.Value);
        tile.Row.ShouldBe(1);
        tile.Col.ShouldBe(3);
        tile.RowSpan.ShouldBe(2);
        tile.ColSpan.ShouldBe(1);
    }

    [Fact]
    public async Task Tiles_keep_the_order_of_the_domain_event_with_and_without_an_overlay()
    {
        FakeEventBus bus = new();
        LayoutRevisionPublishedDomainEventHandler handler = new(bus, new FakeLayoutLifecycleBroadcaster());
        var first = CameraIdentifier.From(Guid.CreateVersion7());
        var second = CameraIdentifier.From(Guid.CreateVersion7());
        var overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        IReadOnlyList<Tile> tiles =
        [
            new Tile(first, Option<OverlayIdentifier>.None, GridPosition.From(1, 3)),
            new Tile(second, Option<OverlayIdentifier>.Some(overlay), GridPosition.From(0, 0)),
        ];

        await handler.Handle(
            EventWith(LayoutIdentifier.New(), tiles, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        var v2 = bus.Published.ShouldHaveSingleItem().ShouldBeOfType<LayoutRevisionPublishedV2>();
        v2.Tiles.Select(tile => tile.Camera).ShouldBe([first.Value, second.Value]);
        v2.Tiles.Select(tile => tile.Overlay).ShouldBe([(Guid?)null, overlay.Value]);
    }

    [Fact]
    public async Task The_published_event_carries_the_revision_name_grid_timestamp_and_publisher()
    {
        FakeEventBus bus = new();
        LayoutRevisionPublishedDomainEventHandler handler = new(bus, new FakeLayoutLifecycleBroadcaster());
        var layout = LayoutIdentifier.New();
        var by = OperatorIdentifier.From(Guid.CreateVersion7());
        IReadOnlyList<Tile> tiles =
        [
            new Tile(CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None, GridPosition.From(0, 0)),
        ];

        await handler.Handle(EventWith(layout, tiles, by), CancellationToken.None);

        var v2 = bus.Published.ShouldHaveSingleItem().ShouldBeOfType<LayoutRevisionPublishedV2>();
        v2.Layout.ShouldBe(layout.Value);
        v2.RevisionNumber.ShouldBe(3);
        v2.Name.ShouldBe("Hero-Wall");
        v2.GridRows.ShouldBe(2);
        v2.GridCols.ShouldBe(4);
        v2.PublishedAt.ShouldBe(FixedMoment);
        v2.PublishedBy.ShouldBe(by.Value);
        v2.Metadata.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task The_lean_notification_names_the_fab_layout_revision_name_and_timestamp()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        LayoutRevisionPublishedDomainEventHandler handler = new(new FakeEventBus(), broadcaster);
        var layout = LayoutIdentifier.New();
        IReadOnlyList<Tile> tiles =
        [
            new Tile(CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None, GridPosition.From(0, 0)),
        ];

        await handler.Handle(
            EventWith(layout, tiles, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        var notification = broadcaster.Published.ShouldHaveSingleItem();
        notification.Fab.ShouldBe(Munich);
        notification.Layout.ShouldBe(layout);
        notification.RevisionNumber.ShouldBe(LayoutRevisionNumber.From(3));
        notification.Name.ShouldBe(LayoutName.From("Hero-Wall"));
        notification.PublishedAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public async Task The_integration_event_is_published_before_the_broadcast()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        BroadcastCountingBus bus = new(broadcaster);
        LayoutRevisionPublishedDomainEventHandler handler = new(bus, broadcaster);
        IReadOnlyList<Tile> tiles =
        [
            new Tile(CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None, GridPosition.From(0, 0)),
        ];

        await handler.Handle(
            EventWith(LayoutIdentifier.New(), tiles, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        bus.BroadcastsBeforeFirstPublish.ShouldBe(0);
        broadcaster.Published.ShouldHaveSingleItem();
    }
}
