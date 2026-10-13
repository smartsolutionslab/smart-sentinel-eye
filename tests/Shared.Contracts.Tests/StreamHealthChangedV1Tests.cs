using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class StreamHealthChangedV1Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        var camera = Guid.CreateVersion7();
        var at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        StreamHealthChangedV1 evt = new(camera, "Healthy", "Degraded", at, "source unreachable", Metadata: TestMetadata);

        evt.Camera.ShouldBe(camera);
        evt.FromState.ShouldBe("Healthy");
        evt.ToState.ShouldBe("Degraded");
        evt.ChangedAt.ShouldBe(at);
        evt.Error.ShouldBe("source unreachable");
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        StreamHealthChangedV1 evt = new(
            Guid.CreateVersion7(),
            "Provisioning",
            "Healthy",
            DateTimeOffset.UtcNow,
            null,
            Metadata: TestMetadata);

        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        var camera = Guid.CreateVersion7();
        var at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        StreamHealthChangedV1 first = new(camera, "Healthy", "Degraded", at, "boom", Metadata: TestMetadata);
        StreamHealthChangedV1 second = new(camera, "Healthy", "Degraded", at, "boom", Metadata: TestMetadata);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Null_error_is_preserved_for_recovery_transitions()
    {
        StreamHealthChangedV1 evt = new(
            Guid.CreateVersion7(),
            "Degraded",
            "Healthy",
            DateTimeOffset.UtcNow,
            null,
            Metadata: TestMetadata);

        evt.Error.ShouldBeNull();
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field_including_null_error()
    {
        var camera = Guid.CreateVersion7();
        var at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);
        StreamHealthChangedV1 original = new(camera, "Degraded", "Healthy", at, null, Metadata: TestMetadata);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<StreamHealthChangedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
