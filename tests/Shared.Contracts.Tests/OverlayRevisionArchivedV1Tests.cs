using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class OverlayRevisionArchivedV1Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        OverlayRevisionArchivedV1 evt = new(overlay, 1, FixedMoment, by);

        evt.Overlay.ShouldBe(overlay);
        evt.RevisionNumber.ShouldBe(1);
        evt.ArchivedAt.ShouldBe(FixedMoment);
        evt.ArchivedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        OverlayRevisionArchivedV1 evt = new(
            Guid.CreateVersion7(), 1, FixedMoment, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        OverlayRevisionArchivedV1 a = new(overlay, 2, FixedMoment, by);
        OverlayRevisionArchivedV1 b = new(overlay, 2, FixedMoment, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        OverlayRevisionArchivedV1 original = new(
            Guid.CreateVersion7(), 3, FixedMoment, Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        OverlayRevisionArchivedV1 deserialized =
            JsonSerializer.Deserialize<OverlayRevisionArchivedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
