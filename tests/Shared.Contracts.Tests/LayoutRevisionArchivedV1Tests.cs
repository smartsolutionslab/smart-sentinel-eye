using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class LayoutRevisionArchivedV1Tests
{
    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid layout = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        LayoutRevisionArchivedV1 evt = new(layout, 1, at, by);

        evt.Layout.ShouldBe(layout);
        evt.RevisionNumber.ShouldBe(1);
        evt.ArchivedAt.ShouldBe(at);
        evt.ArchivedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        LayoutRevisionArchivedV1 evt = new(
            Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid layout = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        LayoutRevisionArchivedV1 a = new(layout, 2, at, by);
        LayoutRevisionArchivedV1 b = new(layout, 2, at, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        LayoutRevisionArchivedV1 original = new(
            Guid.CreateVersion7(),
            7,
            DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture),
            Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        LayoutRevisionArchivedV1 deserialized = JsonSerializer.Deserialize<LayoutRevisionArchivedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
