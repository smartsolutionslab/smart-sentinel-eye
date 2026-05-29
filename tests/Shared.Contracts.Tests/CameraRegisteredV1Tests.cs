using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class CameraRegisteredV1Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        CameraRegisteredV1 evt = new(camera, "Line-1", "rtsp://10.0.5.1/h264", at, operatorId, Metadata: TestMetadata);

        evt.Camera.ShouldBe(camera);
        evt.Name.ShouldBe("Line-1");
        evt.Url.ShouldBe("rtsp://10.0.5.1/h264");
        evt.RegisteredAt.ShouldBe(at);
        evt.RegisteredBy.ShouldBe(operatorId);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(),
            "Line-1",
            "rtsp://10.0.5.1/h264",
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            Metadata: TestMetadata);

        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

        CameraRegisteredV1 first = new(camera, "Line-1", "rtsp://10.0.5.1/h264", at, operatorId, Metadata: TestMetadata);
        CameraRegisteredV1 second = new(camera, "Line-1", "rtsp://10.0.5.1/h264", at, operatorId, Metadata: TestMetadata);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);
        CameraRegisteredV1 original = new(camera, "Line-1", "rtsp://10.0.5.1/h264", at, operatorId, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        CameraRegisteredV1 deserialized = JsonSerializer.Deserialize<CameraRegisteredV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
