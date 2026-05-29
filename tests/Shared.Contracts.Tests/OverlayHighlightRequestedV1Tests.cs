using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class OverlayHighlightRequestedV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid causing = Guid.CreateVersion7();
        OverlayHighlightRequestedV1 evt = new(overlay, 10_000, Moment, causing, Metadata: TestMetadata);

        evt.OverlayIdentifier.ShouldBe(overlay);
        evt.DurationMs.ShouldBe(10_000);
        evt.RequestedAt.ShouldBe(Moment);
        evt.CausingEventIdentifier.ShouldBe(causing);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        OverlayHighlightRequestedV1 evt =
            new(Guid.CreateVersion7(), 5_000, Moment, Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid causing = Guid.CreateVersion7();
        OverlayHighlightRequestedV1 a = new(overlay, 5_000, Moment, causing, Metadata: TestMetadata);
        OverlayHighlightRequestedV1 b = new(overlay, 5_000, Moment, causing, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        OverlayHighlightRequestedV1 original =
            new(Guid.CreateVersion7(), 10_000, Moment, Guid.CreateVersion7(), Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        OverlayHighlightRequestedV1 deserialized =
            JsonSerializer.Deserialize<OverlayHighlightRequestedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
