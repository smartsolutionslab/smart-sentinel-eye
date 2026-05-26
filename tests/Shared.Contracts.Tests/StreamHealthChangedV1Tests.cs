using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class StreamHealthChangedV1Tests
{
    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid camera = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        StreamHealthChangedV1 evt = new(camera, "Healthy", "Degraded", at, "source unreachable");

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
            null);

        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid camera = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        StreamHealthChangedV1 first = new(camera, "Healthy", "Degraded", at, "boom");
        StreamHealthChangedV1 second = new(camera, "Healthy", "Degraded", at, "boom");

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
            null);

        evt.Error.ShouldBeNull();
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field_including_null_error()
    {
        Guid camera = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);
        StreamHealthChangedV1 original = new(camera, "Degraded", "Healthy", at, null);

        string json = JsonSerializer.Serialize(original);
        StreamHealthChangedV1 deserialized = JsonSerializer.Deserialize<StreamHealthChangedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
