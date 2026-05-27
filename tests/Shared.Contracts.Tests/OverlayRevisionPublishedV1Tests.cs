using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class OverlayRevisionPublishedV1Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        OverlayRevisionPublishedV1 evt = new(
            overlay, 1, "Line-1 Title",
            "Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48,
            FixedMoment, by);

        evt.Overlay.ShouldBe(overlay);
        evt.RevisionNumber.ShouldBe(1);
        evt.Name.ShouldBe("Line-1 Title");
        evt.Text.ShouldBe("Production Line 1");
        evt.NormalizedX.ShouldBe(0.5m);
        evt.NormalizedY.ShouldBe(0.05m);
        evt.NormalizedWidth.ShouldBe(0.3m);
        evt.NormalizedHeight.ShouldBe(0.08m);
        evt.FontSizePx.ShouldBe(48);
        evt.PublishedAt.ShouldBe(FixedMoment);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        OverlayRevisionPublishedV1 evt = new(
            Guid.CreateVersion7(), 1, "Line-1", "Hello",
            0.1m, 0.2m, 0.3m, 0.4m, 32, FixedMoment, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        OverlayRevisionPublishedV1 a = new(overlay, 2, "Line-1", "Hello",
            0.1m, 0.2m, 0.3m, 0.4m, 32, FixedMoment, by);
        OverlayRevisionPublishedV1 b = new(overlay, 2, "Line-1", "Hello",
            0.1m, 0.2m, 0.3m, 0.4m, 32, FixedMoment, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        OverlayRevisionPublishedV1 original = new(
            Guid.CreateVersion7(), 3, "Line-1 Title", "Hello",
            0.1m, 0.2m, 0.3m, 0.4m, 32, FixedMoment, Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        OverlayRevisionPublishedV1 deserialized =
            JsonSerializer.Deserialize<OverlayRevisionPublishedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
