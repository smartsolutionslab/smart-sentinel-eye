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
            Fab: FabIdentifier.From("munich"),
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
            Fab: FabIdentifier.From("munich"),
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
            Fab: FabIdentifier.From("munich"),
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
            Fab: FabIdentifier.From("munich"),
            Name: CameraName.From("Cam-Duplicate"),
            Url: RtspUrl.From("rtsp://10.0.5.50/h264"),
            RegisteredBy: AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        RegisterCameraCommand second = new(
            Fab: FabIdentifier.From("munich"),
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
            new RegisterCameraCommand(FabIdentifier.From("munich"), 
                Name: CameraName.From("Line-1-Entrance"),
                Url: RtspUrl.From("rtsp://10.0.5.12/h264"),
                RegisteredBy: AnAdmin),
            CancellationToken.None);

        Result<CameraIdentifier, RegisterCameraError> result = await handler.HandleAsync(
            new RegisterCameraCommand(FabIdentifier.From("munich"), 
                Name: CameraName.From("line-1-entrance"),
                Url: RtspUrl.From("rtsp://10.0.5.13/h264"),
                RegisteredBy: AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RegisterCameraError.NameAlreadyTaken>();
    }

    private static RegisterCameraCommandHandler NewHandler(InMemoryCameraRepository cameras) =>
        new(cameras, new FixedClock(FixedMoment), NullLogger<RegisterCameraCommandHandler>.Instance);

    // ---- spec 015 T013: the name is unique per fab, not globally ----

    [Fact]
    public async Task The_same_name_is_accepted_in_a_second_fab()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand inMunich = new(
            Fab: FabIdentifier.From("munich"),
            Name: CameraName.From("Line-1-North"),
            Url: RtspUrl.From("rtsp://10.0.5.60/h264"),
            RegisteredBy: AnAdmin);
        (await handler.HandleAsync(inMunich, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        RegisterCameraCommand inDresden = new(
            Fab: FabIdentifier.From("dresden"),
            Name: CameraName.From("Line-1-North"),
            Url: RtspUrl.From("rtsp://10.0.5.61/h264"),
            RegisteredBy: AnAdmin);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(inDresden, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.Count.ShouldBe(2);
        cameras.Cameras.Select(camera => camera.Fab.Value).ShouldBe(["munich", "dresden"]);
    }

    [Fact]
    public async Task The_same_name_is_refused_in_the_same_fab_and_the_refusal_names_it()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand first = new(
            Fab: FabIdentifier.From("dresden"),
            Name: CameraName.From("Line-1-North"),
            Url: RtspUrl.From("rtsp://10.0.5.60/h264"),
            RegisteredBy: AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        Result<CameraIdentifier, RegisterCameraError> result =
            await handler.HandleAsync(first with { Url = RtspUrl.From("rtsp://10.0.5.61/h264") },
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RegisterCameraError.NameAlreadyTaken>();
        // Named, so a multi-fab operator can tell which of their plants
        // refused them rather than guessing — the same name is legitimately
        // free in another of theirs.
        result.Error.Message.ShouldContain("dresden");
        result.Error.Message.ShouldContain("Line-1-North");
    }

    // ---- spec 028 FR-006: a retired camera does not hold its name ----

    /// <summary>
    /// The gap research §1 missed. The partial unique index has always excluded
    /// Decommissioned rows, so the schema would accept the insert — but the
    /// handler asks <c>ExistsByNameAsync</c> first, and that predicate counted
    /// retired cameras. The refusal came from the application, not the
    /// database, which is why reading the index said nothing about it.
    /// </summary>
    [Fact]
    public async Task A_retired_cameras_name_does_not_block_a_new_registration()
    {
        InMemoryCameraRepository cameras = new();
        RegisterCameraCommandHandler handler = NewHandler(cameras);

        RegisterCameraCommand original = new(
            Fab: FabIdentifier.From("munich"),
            Name: CameraName.From("Line-3-Inlet"),
            Url: RtspUrl.From("rtsp://10.0.5.31/h264"),
            RegisteredBy: AnAdmin);

        Result<CameraIdentifier, RegisterCameraError> registered =
            await handler.HandleAsync(original, CancellationToken.None);
        registered.IsSuccess.ShouldBeTrue();

        // Refused while it is still active — the control, so the acceptance
        // below is not simply "uniqueness is not enforced".
        (await handler.HandleAsync(original, CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        cameras.Cameras
            .Single(camera => camera.Id.Equals(registered.Value))
            .Retire(AnAdmin, new FixedClock(FixedMoment));

        // Differently cased on purpose: the name is released as normalised, so
        // reuse must not depend on matching the retired camera's spelling.
        RegisterCameraCommand replacement = original with { Name = CameraName.From("line-3-inlet") };

        (await handler.HandleAsync(replacement, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }
}
