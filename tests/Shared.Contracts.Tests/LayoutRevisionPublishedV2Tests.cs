using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class LayoutRevisionPublishedV2Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static IReadOnlyList<LayoutTileV2> Tiles(Guid camera, Guid? overlay) =>
        [new LayoutTileV2(camera, overlay, 0, 0), new LayoutTileV2(Guid.CreateVersion7(), null, 0, 1)];

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid layout = Guid.CreateVersion7();
        Guid camera = Guid.CreateVersion7();
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        LayoutRevisionPublishedV2 evt = new(
            layout, 1, "Line-1", Tiles(camera, overlay), 1, 2, at, by, Metadata: TestMetadata);

        evt.Layout.ShouldBe(layout);
        evt.RevisionNumber.ShouldBe(1);
        evt.Name.ShouldBe("Line-1");
        evt.GridRows.ShouldBe(1);
        evt.GridCols.ShouldBe(2);
        evt.Tiles.Count.ShouldBe(2);
        evt.Tiles[0].Camera.ShouldBe(camera);
        evt.Tiles[0].Overlay.ShouldBe(overlay);
        evt.Tiles[0].Row.ShouldBe(0);
        evt.Tiles[0].Col.ShouldBe(0);
        evt.PublishedAt.ShouldBe(at);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        LayoutRevisionPublishedV2 evt = new(
            Guid.CreateVersion7(), 1, "Line-1", Tiles(Guid.CreateVersion7(), null), 2, 2,
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field_including_the_tile_list()
    {
        LayoutRevisionPublishedV2 original = new(
            Guid.CreateVersion7(),
            3,
            "Line-1",
            Tiles(Guid.CreateVersion7(), Guid.CreateVersion7()),
            1,
            2,
            DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture),
            Guid.CreateVersion7(),
            Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        LayoutRevisionPublishedV2 deserialized = JsonSerializer.Deserialize<LayoutRevisionPublishedV2>(json)!;

        deserialized.Layout.ShouldBe(original.Layout);
        deserialized.GridRows.ShouldBe(original.GridRows);
        deserialized.GridCols.ShouldBe(original.GridCols);
        deserialized.Tiles.Count.ShouldBe(original.Tiles.Count);
        deserialized.Tiles[0].ShouldBe(original.Tiles[0]);
        deserialized.Tiles[1].ShouldBe(original.Tiles[1]);
    }
}
