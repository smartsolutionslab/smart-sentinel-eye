using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.Shared.Contracts.Tests;

/// <summary>
/// Spec 300 (#2349), ADR-0165, T007: <c>OverlayRevisionPublishedV3</c>
/// replaces <c>OverlayRevisionPublishedV2</c> in a clean cut — the revision
/// now carries an ordered set of elements with a kind and colour instead of
/// a flat label. Primitive types only at the wire boundary (ADR-0040); a
/// non-text element carries <c>null</c> <c>Text</c>/<c>FontSizePx</c>.
/// </summary>
public class OverlayRevisionPublishedV3Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        List<OverlayElementV3> elements = [
            new OverlayElementV3("Box", "#D32F2FFF", 0.1m, 0.2m, 0.4m, 0.5m, null, null),
            new OverlayElementV3("Text", "#D32F2FFF", 0.1m, 0.72m, 0.4m, 0.08m, "Zone A", 24),
        ];

        OverlayRevisionPublishedV3 evt = new(
            overlay, 1, "Line-1 Title", elements, FixedMoment, by, Metadata: TestMetadata);

        evt.Overlay.ShouldBe(overlay);
        evt.RevisionNumber.ShouldBe(1);
        evt.Name.ShouldBe("Line-1 Title");
        evt.Elements.ShouldBe(elements);
        evt.PublishedAt.ShouldBe(FixedMoment);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void A_non_text_element_carries_null_text_and_font_size()
    {
        OverlayElementV3 box = new("Box", "#D32F2FFF", 0.1m, 0.2m, 0.4m, 0.5m, null, null);

        box.Kind.ShouldBe("Box");
        box.Text.ShouldBeNull();
        box.FontSizePx.ShouldBeNull();
    }

    [Fact]
    public void A_text_element_carries_its_text_and_font_size()
    {
        OverlayElementV3 text = new("Text", "#D32F2FFF", 0.1m, 0.72m, 0.4m, 0.08m, "Zone A", 24);

        text.Kind.ShouldBe("Text");
        text.Text.ShouldBe("Zone A");
        text.FontSizePx.ShouldBe(24);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        List<OverlayElementV3> elements = [
            new OverlayElementV3("Box", "#D32F2FFF", 0.1m, 0.2m, 0.4m, 0.5m, null, null),
            new OverlayElementV3("Text", "#D32F2FFF", 0.1m, 0.72m, 0.4m, 0.08m, "Zone A", 24),
        ];
        OverlayRevisionPublishedV3 evt = new(
            Guid.CreateVersion7(), 1, "Line-1", elements, FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        List<OverlayElementV3> elements = [
            new OverlayElementV3("Box", "#D32F2FFF", 0.1m, 0.2m, 0.4m, 0.5m, null, null),
            new OverlayElementV3("Text", "#D32F2FFF", 0.1m, 0.72m, 0.4m, 0.08m, "Zone A", 24),
        ];

        OverlayRevisionPublishedV3 a = new(overlay, 2, "Line-1", elements, FixedMoment, by, Metadata: TestMetadata);
        OverlayRevisionPublishedV3 b = new(overlay, 2, "Line-1", elements, FixedMoment, by, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_element_in_order_including_a_null_text()
    {
        List<OverlayElementV3> elements = [
            new OverlayElementV3("Box", "#D32F2FFF", 0.1m, 0.2m, 0.4m, 0.5m, null, null),
            new OverlayElementV3("Text", "#D32F2FFF", 0.1m, 0.72m, 0.4m, 0.08m, "Zone A", 24),
        ];
        OverlayRevisionPublishedV3 original = new(
            Guid.CreateVersion7(), 3, "Line-1 Title", elements, FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        OverlayRevisionPublishedV3 deserialized =
            JsonSerializer.Deserialize<OverlayRevisionPublishedV3>(json)!;

        deserialized.Overlay.ShouldBe(original.Overlay);
        deserialized.RevisionNumber.ShouldBe(original.RevisionNumber);
        deserialized.Name.ShouldBe(original.Name);
        deserialized.Elements.Count.ShouldBe(2);
        deserialized.Elements[0].ShouldBe(original.Elements[0]);
        deserialized.Elements[1].ShouldBe(original.Elements[1]);
        deserialized.Elements[0].Kind.ShouldBe("Box");
        deserialized.Elements[0].Text.ShouldBeNull();
        deserialized.Elements[1].Kind.ShouldBe("Text");
        deserialized.Elements[1].Text.ShouldBe("Zone A");
        deserialized.Elements[1].FontSizePx.ShouldBe(24);
    }
}
