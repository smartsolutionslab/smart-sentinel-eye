using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Kernel;
using CameraAggregate = SmartSentinelEye.CameraCatalog.Domain.Camera.Camera;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 028 T007 — retiring a camera (#1433).
/// </summary>
public class RetireCameraCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-24T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Retires_a_camera_in_the_callers_fab()
    {
        InMemoryCameraRepository cameras = new();
        CameraAggregate camera = await SeedAsync(cameras, "munich", "line-3-inlet");

        Result<CameraIdentifier, RetireCameraError> result =
            await Handler(cameras).HandleAsync(Command("munich", camera.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        camera.Status.ShouldBe(CameraStatus.Decommissioned);
    }

    [Fact]
    public async Task An_unknown_camera_is_not_found()
    {
        InMemoryCameraRepository cameras = new();

        Result<CameraIdentifier, RetireCameraError> result = await Handler(cameras)
            .HandleAsync(Command("munich", CameraIdentifier.New()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
    }

    /// <summary>
    /// FR-004. The camera exists; the caller may not know that. The refusal
    /// must be the same one an identifier naming nothing produces, because a
    /// distinguishable answer lets an operator enumerate another plant's
    /// cameras — and a camera's record carries its RTSP address.
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_as_not_found_and_stays_registered()
    {
        InMemoryCameraRepository cameras = new();
        CameraAggregate dresden = await SeedAsync(cameras, "dresden", "line-3-inlet");

        Result<CameraIdentifier, RetireCameraError> result =
            await Handler(cameras).HandleAsync(Command("munich", dresden.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");

        // The message must not differ either — the fab is what it would leak.
        result.Error.Message.ShouldBe($"No camera '{dresden.Id.Value}' exists.");

        // And nothing happened to it.
        dresden.Status.ShouldBe(CameraStatus.Registered);
    }

    /// <summary>
    /// FR-005, asserted on the <b>event count</b> rather than the return value.
    /// A handler that succeeds while raising again announces two retirements to
    /// every consumer, and the audit trail records the camera retired twice —
    /// while the endpoint answers 204 both times and looks entirely correct.
    /// </summary>
    [Fact]
    public async Task Retiring_twice_succeeds_and_announces_once()
    {
        InMemoryCameraRepository cameras = new();
        CameraAggregate camera = await SeedAsync(cameras, "munich", "line-3-inlet");
        RetireCameraCommand command = Command("munich", camera.Id);

        Result<CameraIdentifier, RetireCameraError> first =
            await Handler(cameras).HandleAsync(command, CancellationToken.None);
        Result<CameraIdentifier, RetireCameraError> second =
            await Handler(cameras).HandleAsync(command, CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue("retiring an already-retired camera is the outcome the caller asked for");

        camera.PendingEvents.OfType<CameraRetiredDomainEvent>().Count().ShouldBe(1);
    }

    private static RetireCameraCommandHandler Handler(InMemoryCameraRepository cameras) =>
        new(cameras, new FixedClock(Now), NullLogger<RetireCameraCommandHandler>.Instance);

    private static RetireCameraCommand Command(string fab, CameraIdentifier camera) =>
        new(FabIdentifier.From(fab), camera, Operator);

    private static async Task<CameraAggregate> SeedAsync(
        InMemoryCameraRepository cameras, string fab, string name)
    {
        CameraAggregate camera = CameraAggregate.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.0.1:554/h264"),
            Operator,
            new FixedClock(Now));

        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);
        camera.ClearPendingEvents();

        return camera;
    }
}
