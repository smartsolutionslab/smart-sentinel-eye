using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class OverlayRevisionPublishedV2Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static IReadOnlyList<OverlayLabelV2> ThreeLabels() =>
    [
        new OverlayLabelV2("First", 0.1m, 0.1m, 0.2m, 0.2m, 16),
        new OverlayLabelV2("Second", 0.2m, 0.2m, 0.2m, 0.2m, 20),
        new OverlayLabelV2("Third", 0.3m, 0.3m, 0.2m, 0.2m, 24),
    ];

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        IReadOnlyList<OverlayLabelV2> labels = ThreeLabels();

        OverlayRevisionPublishedV2 evt = new(
            overlay, 1, "Line-1 Title", labels, FixedMoment, by, Metadata: TestMetadata);

        evt.Overlay.ShouldBe(overlay);
        evt.RevisionNumber.ShouldBe(1);
        evt.Name.ShouldBe("Line-1 Title");
        evt.Labels.ShouldBe(labels);
        evt.PublishedAt.ShouldBe(FixedMoment);
        evt.PublishedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        OverlayRevisionPublishedV2 evt = new(
            Guid.CreateVersion7(), 1, "Line-1", ThreeLabels(), FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        IReadOnlyList<OverlayLabelV2> labels = ThreeLabels();

        OverlayRevisionPublishedV2 a = new(overlay, 2, "Line-1", labels, FixedMoment, by, Metadata: TestMetadata);
        OverlayRevisionPublishedV2 b = new(overlay, 2, "Line-1", labels, FixedMoment, by, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_label_in_order()
    {
        OverlayRevisionPublishedV2 original = new(
            Guid.CreateVersion7(), 3, "Line-1 Title", ThreeLabels(), FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        OverlayRevisionPublishedV2 deserialized =
            JsonSerializer.Deserialize<OverlayRevisionPublishedV2>(json)!;

        deserialized.Overlay.ShouldBe(original.Overlay);
        deserialized.RevisionNumber.ShouldBe(original.RevisionNumber);
        deserialized.Name.ShouldBe(original.Name);
        deserialized.Labels.Count.ShouldBe(3);
        deserialized.Labels[0].ShouldBe(original.Labels[0]);
        deserialized.Labels[1].ShouldBe(original.Labels[1]);
        deserialized.Labels[2].ShouldBe(original.Labels[2]);
        deserialized.Labels[0].Text.ShouldBe("First");
        deserialized.Labels[2].Text.ShouldBe("Third");
    }
}
