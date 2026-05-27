using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class LayoutRevisionPublishedV1Tests
{
    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid layout = Guid.CreateVersion7();
        Guid camera = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        LayoutRevisionPublishedV1 evt = new(layout, 1, "Line-1", camera, at, by);

        evt.Layout.ShouldBe(layout);
        evt.RevisionNumber.ShouldBe(1);
        evt.Name.ShouldBe("Line-1");
        evt.Camera.ShouldBe(camera);
        evt.PublishedAt.ShouldBe(at);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        LayoutRevisionPublishedV1 evt = new(
            Guid.CreateVersion7(), 1, "Line-1", Guid.CreateVersion7(), DateTimeOffset.UtcNow, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid layout = Guid.CreateVersion7();
        Guid camera = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        LayoutRevisionPublishedV1 a = new(layout, 2, "Line-1", camera, at, by);
        LayoutRevisionPublishedV1 b = new(layout, 2, "Line-1", camera, at, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        LayoutRevisionPublishedV1 original = new(
            Guid.CreateVersion7(),
            3,
            "Line-1",
            Guid.CreateVersion7(),
            DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture),
            Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        LayoutRevisionPublishedV1 deserialized = JsonSerializer.Deserialize<LayoutRevisionPublishedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
