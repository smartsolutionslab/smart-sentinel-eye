using System.Globalization;
using System.Net;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Spec 029 T005 — reading one camera (FR-001, FR-002, FR-006).
/// </summary>
public class GetCameraQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_camera_in_the_callers_fab_is_returned_with_its_version()
    {
        Camera camera = RegisterIn("munich", "Line-7");
        GetCameraQueryHandler handler = NewHandler(camera);

        Result<CameraDto, GetCameraError> result =
            await handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CameraIdentifier.ShouldBe(camera.Id.Value);
        result.Value.Fab.ShouldBe("munich");
        result.Value.Name.ShouldBe("Line-7");
        result.Value.RegisteredAt.ShouldBe(camera.RegisteredAt);

        // The aggregate's own version, not a constant — this is the value a
        // caller has to quote to change the camera, and nothing exposed it
        // before this feature.
        result.Value.Version.ShouldBe(camera.Version);
    }

    /// <summary>
    /// FR-002. Retirement takes a camera out of the default listing; it does
    /// not make its record unreadable. "Show me what is out there" and "tell me
    /// about this camera" are different questions.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_is_returned_with_its_status()
    {
        Camera camera = RegisterIn("munich", "Line-Gone");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        GetCameraQueryHandler handler = NewHandler(camera);

        Result<CameraDto, GetCameraError> result =
            await handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe("Decommissioned");
    }

    [Fact]
    public async Task An_identifier_that_names_nothing_is_not_found()
    {
        GetCameraQueryHandler handler = NewHandler();

        Result<CameraDto, GetCameraError> result = await handler.HandleAsync(
            new GetCameraQuery([FabIdentifier.From("munich")], CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetCameraError.CameraNotFound>();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// FR-006, and the reason <see cref="GetCameraError"/> has exactly one
    /// case. The refusal for another plant's camera must be the *same* error,
    /// not a sibling with a different message — a camera record carries its
    /// RTSP address, so anything distinguishable lets an operator enumerate
    /// another plant's cameras one request at a time.
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is()
    {
        Camera inMunich = RegisterIn("munich", "Line-Secret");
        GetCameraQueryHandler handler = NewHandler(inMunich);

        Result<CameraDto, GetCameraError> crossFab =
            await handler.HandleAsync(QueryFor(inMunich, "dresden"), CancellationToken.None);

        Result<CameraDto, GetCameraError> neverExisted = await handler.HandleAsync(
            new GetCameraQuery([FabIdentifier.From("dresden")], CameraIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        crossFab.IsFailure.ShouldBeTrue();
        neverExisted.IsFailure.ShouldBeTrue();

        // The same type and the same code — not merely both failures. A test
        // that only asserted IsFailure would pass against a distinct
        // "not yours" error, which is the defect.
        crossFab.Error.ShouldBeOfType<GetCameraError.CameraNotFound>();
        crossFab.Error.Code.ShouldBe(neverExisted.Error.Code);
        crossFab.Error.Status.ShouldBe(neverExisted.Error.Status);
    }

    [Fact]
    public async Task An_operator_holding_both_fabs_can_read_the_camera()
    {
        Camera inMunich = RegisterIn("munich", "Line-Shared");
        GetCameraQueryHandler handler = NewHandler(inMunich);

        Result<CameraDto, GetCameraError> result = await handler.HandleAsync(
            new GetCameraQuery(
                [FabIdentifier.From("dresden"), FabIdentifier.From("munich")],
                inMunich.Id),
            CancellationToken.None);

        // Without this the refusal above could be a blanket denial rather than
        // fab scoping, and both tests would still pass.
        result.IsSuccess.ShouldBeTrue();
        result.Value.Fab.ShouldBe("munich");
    }

    private static GetCameraQuery QueryFor(Camera camera, string fab) =>
        new([FabIdentifier.From(fab)], camera.Id);

    private static GetCameraQueryHandler NewHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));

    private static Camera RegisterIn(string fab, string name) =>
        Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.5.12/h264"),
            AnAdmin,
            new FixedClock(FixedMoment));
}
