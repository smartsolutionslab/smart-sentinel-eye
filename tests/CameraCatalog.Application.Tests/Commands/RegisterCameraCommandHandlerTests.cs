using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

public class RegisterCameraCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Register_a_camera_with_valid_input_returns_the_new_identifier()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand command = new(
            Name: CameraName.From("Line-1-Entrance"),
            Url: RtspUrl.From("rtsp://10.0.5.12/h264"),
            RegisteredBy: AnAdmin);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Register_a_camera_persists_the_aggregate_and_calls_save_once()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand command = new(
            Name: CameraName.From("Line-2-East"),
            Url: RtspUrl.From("rtsp://10.0.5.22/h264"),
            RegisteredBy: AnAdmin);

        await handler.HandleAsync(command, CancellationToken.None);

        cameras.Cameras.Count.ShouldBe(1);
        cameras.SaveCallCount.ShouldBe(1);
        cameras.Cameras.Single().Name.Value.ShouldBe("Line-2-East");
    }

    [Fact]
    public async Task Register_a_camera_raises_a_pending_domain_event_on_the_aggregate()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand command = new(
            Name: CameraName.From("Cam-Event-Test"),
            Url: RtspUrl.From("rtsp://10.0.5.30/h264"),
            RegisteredBy: AnAdmin);

        await handler.HandleAsync(command, CancellationToken.None);

        cameras.Cameras.Single().PendingEvents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Register_a_camera_with_a_duplicate_name_returns_NameAlreadyTaken()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand first = new(
            Name: CameraName.From("Cam-Duplicate"),
            Url: RtspUrl.From("rtsp://10.0.5.50/h264"),
            RegisteredBy: AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        RegisterCameraCommand second = new(
            Name: CameraName.From("Cam-Duplicate"),
            Url: RtspUrl.From("rtsp://10.0.5.51/h264"),
            RegisteredBy: AnAdmin);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(second, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RegisterCameraError.NameAlreadyTaken>();
        result.Error.Code.ShouldBe("CAMERA_NAME_TAKEN");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        cameras.Cameras.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Register_a_camera_with_case_differing_duplicate_name_returns_NameAlreadyTaken()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        await handler.HandleAsync(
            new RegisterCameraCommand(
                Name: CameraName.From("Line-1-Entrance"),
                Url: RtspUrl.From("rtsp://10.0.5.12/h264"),
                RegisteredBy: AnAdmin),
            CancellationToken.None);

        Result<CameraIdentifier, RegisterCameraError> result = await handler.HandleAsync(
            new RegisterCameraCommand(
                Name: CameraName.From("line-1-entrance"),
                Url: RtspUrl.From("rtsp://10.0.5.13/h264"),
                RegisteredBy: AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RegisterCameraError.NameAlreadyTaken>();
    }

    private static RegisterCameraCommandHandler NewHandler(InMemoryCameraRepository cameras) =>
        new(cameras, new FixedClock(FixedMoment), NullLogger<RegisterCameraCommandHandler>.Instance);
}
