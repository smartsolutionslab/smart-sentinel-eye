using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests.SystemVariables;

public class ResolvedOverlayTextChangedV2Tests
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
        IReadOnlyList<string> texts = ["OEE: 82.5%", "Line 1", "Running"];
        ResolvedOverlayTextChangedV2 evt = new(overlay, texts, 7, Metadata: TestMetadata);

        evt.Overlay.ShouldBe(overlay);
        evt.ResolvedTexts.ShouldBe(texts);
        evt.Version.ShouldBe(7);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        ResolvedOverlayTextChangedV2 evt = new(Guid.CreateVersion7(), ["x"], 1, Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        IReadOnlyList<string> texts = ["v"];
        ResolvedOverlayTextChangedV2 a = new(overlay, texts, 3, Metadata: TestMetadata);
        ResolvedOverlayTextChangedV2 b = new(overlay, texts, 3, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void A_single_version_bump_covers_the_whole_resolved_set()
    {
        IReadOnlyList<string> texts = ["First", "Second", "Third"];
        ResolvedOverlayTextChangedV2 evt = new(Guid.CreateVersion7(), texts, 9, Metadata: TestMetadata);

        evt.ResolvedTexts.Count.ShouldBe(3);
        evt.Version.ShouldBe(9);
    }

    [Fact]
    public void JSON_round_trip_preserves_every_resolved_text_in_order()
    {
        ResolvedOverlayTextChangedV2 original =
            new(Guid.CreateVersion7(), ["OEE: 82.5%", "Line 1"], 9, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        ResolvedOverlayTextChangedV2 deserialized =
            JsonSerializer.Deserialize<ResolvedOverlayTextChangedV2>(json)!;

        deserialized.Overlay.ShouldBe(original.Overlay);
        deserialized.Version.ShouldBe(original.Version);
        deserialized.ResolvedTexts.Count.ShouldBe(2);
        deserialized.ResolvedTexts[0].ShouldBe("OEE: 82.5%");
        deserialized.ResolvedTexts[1].ShouldBe("Line 1");
    }
}
