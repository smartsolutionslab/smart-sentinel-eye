using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests.SystemVariables;

public class ResolvedOverlayTextChangedV1Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV1 evt = new(overlay, "OEE: 82.5%", 7, Metadata: TestMetadata);

        evt.Overlay.ShouldBe(overlay);
        evt.ResolvedText.ShouldBe("OEE: 82.5%");
        evt.Version.ShouldBe(7);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        ResolvedOverlayTextChangedV1 evt = new(Guid.CreateVersion7(), "x", 1, Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV1 a = new(overlay, "v", 3, Metadata: TestMetadata);
        ResolvedOverlayTextChangedV1 b = new(overlay, "v", 3, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        ResolvedOverlayTextChangedV1 original =
            new(Guid.CreateVersion7(), "OEE: 82.5%", 9, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        ResolvedOverlayTextChangedV1 deserialized =
            JsonSerializer.Deserialize<ResolvedOverlayTextChangedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
