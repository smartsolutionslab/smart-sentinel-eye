using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.Shared.Contracts.Tests.Identity;

public class DeviceRegisteredV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-29T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid id = Guid.CreateVersion7();
        DeviceRegisteredV1 evt = new(id, "plc-station-4", "plc", "station-4", "munich", Moment, Metadata: TestMetadata);

        evt.RegisteredClientIdentifier.ShouldBe(id);
        evt.ClientId.ShouldBe("plc-station-4");
        evt.DeviceType.ShouldBe("plc");
        evt.DeviceIdentifier.ShouldBe("station-4");
        evt.Fab.ShouldBe("munich");
        evt.RegisteredAt.ShouldBe(Moment);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new DeviceRegisteredV1(Guid.CreateVersion7(), "x", "plc", "s", "f", Moment, Metadata: TestMetadata)
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid id = Guid.CreateVersion7();
        DeviceRegisteredV1 a = new(id, "x", "plc", "s", "f", Moment, Metadata: TestMetadata);
        DeviceRegisteredV1 b = new(id, "x", "plc", "s", "f", Moment, Metadata: TestMetadata);
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        DeviceRegisteredV1 original = new(
            Guid.CreateVersion7(), "plc-station-4", "plc", "station-4", "munich", Moment, Metadata: TestMetadata);
        string json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<DeviceRegisteredV1>(json).ShouldBe(original);
    }
}
