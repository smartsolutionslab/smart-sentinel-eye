using System.Globalization;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

public class CameraTests
{
    [Fact]
    public void Register_a_camera_with_valid_input_assigns_a_new_identifier()
    {
        Domain.Camera.Camera camera = new CameraBuilder()
            .WithName("Line-1-Entrance")
            .WithUrl("rtsp://10.0.5.12/h264")
            .Build();

        camera.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Register_a_camera_sets_status_to_Registered()
    {
        Domain.Camera.Camera camera = new CameraBuilder().Build();

        camera.Status.ShouldBe(CameraStatus.Registered);
    }

    [Fact]
    public void Register_a_camera_records_the_registration_timestamp_from_the_clock()
    {
        DateTimeOffset moment = DateTimeOffset.Parse("2026-05-25T14:30:00Z", CultureInfo.InvariantCulture);

        Domain.Camera.Camera camera = new CameraBuilder().At(moment).Build();

        camera.RegisteredAt.ShouldBe(moment);
    }

    [Fact]
    public void Register_a_camera_raises_exactly_one_CameraRegisteredDomainEvent()
    {
        Domain.Camera.Camera camera = new CameraBuilder()
            .WithName("Cam-A")
            .WithUrl("rtsp://cam.local/stream")
            .Build();

        camera.PendingEvents.Count.ShouldBe(1);
        camera.PendingEvents.Single().ShouldBeOfType<CameraRegisteredDomainEvent>();
    }

    [Fact]
    public void The_raised_event_carries_the_cameras_identifier_name_url_and_registration_metadata()
    {
        DateTimeOffset moment = DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture);

        Domain.Camera.Camera camera = new CameraBuilder()
            .WithName("Line-3-South")
            .WithUrl("rtsp://10.0.5.30/h264")
            .At(moment)
            .Build();

        CameraRegisteredDomainEvent raised =
            camera.PendingEvents.Single().ShouldBeOfType<CameraRegisteredDomainEvent>();

        raised.Camera.ShouldBe(camera.Id);
        raised.Name.Value.ShouldBe("Line-3-South");
        raised.Url.Value.ShouldBe("rtsp://10.0.5.30/h264");
        raised.RegisteredAt.ShouldBe(moment);
        raised.RegisteredBy.ShouldBe(camera.RegisteredBy);
    }

    [Fact]
    public void Clearing_pending_events_empties_the_aggregate_event_list()
    {
        Domain.Camera.Camera camera = new CameraBuilder().Build();
        camera.PendingEvents.Count.ShouldBe(1);

        camera.ClearPendingEvents();

        camera.PendingEvents.ShouldBeEmpty();
    }
}
