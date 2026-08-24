using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 029 T015 — correcting a camera's address (FR-003 through FR-006).
/// </summary>
public class ChangeCameraAddressCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalUrl = "rtsp://10.0.5.12/h264";
    private const string CorrectedUrl = "rtsp://10.0.5.44/h264";

    [Fact]
    public async Task Correcting_the_address_stores_it_and_saves_once()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = await SeedAsync(cameras, "munich");

        Result<CameraIdentifier, ChangeCameraAddressError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.Single().Url.Value.ShouldBe(CorrectedUrl);
    }

    [Fact]
    public async Task An_unknown_camera_is_not_found()
    {
        InMemoryCameraRepository cameras = new();

        Result<CameraIdentifier, ChangeCameraAddressError> result = await NewHandler(cameras).HandleAsync(
            new ChangeCameraAddressCommand(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                RtspUrl.From(CorrectedUrl),
                ExpectedVersion: 0,
                AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
    }

    /// <summary>
    /// FR-006. The same error as an identifier that names nothing, not a
    /// sibling with a different message — a camera record carries its RTSP
    /// address, so a distinguishable refusal is an enumeration oracle.
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is()
    {
        InMemoryCameraRepository cameras = new();
        Camera inMunich = await SeedAsync(cameras, "munich");

        Result<CameraIdentifier, ChangeCameraAddressError> crossFab =
            await NewHandler(cameras).HandleAsync(CommandFor(inMunich, "dresden"), CancellationToken.None);

        crossFab.IsFailure.ShouldBeTrue();
        crossFab.Error.ShouldBeOfType<ChangeCameraAddressError.CameraNotFound>();
        crossFab.Error.Status.ShouldBe(HttpStatusCode.NotFound);

        // Refused means untouched — a leak that also half-applied would be worse.
        cameras.Cameras.Single().Url.Value.ShouldBe(OriginalUrl);
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_changes_nothing()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = await SeedAsync(cameras, "munich");

        Result<CameraIdentifier, ChangeCameraAddressError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { ExpectedVersion = camera.Version + 7 },
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ChangeCameraAddressError.VersionMismatch mismatch =
            result.Error.ShouldBeOfType<ChangeCameraAddressError.VersionMismatch>();

        // The actual version is reported so the caller knows what to re-read to.
        mismatch.Actual.ShouldBe(camera.Version);
        mismatch.Status.ShouldBe(HttpStatusCode.PreconditionFailed);

        cameras.Cameras.Single().Url.Value.ShouldBe(OriginalUrl);
    }

    /// <summary>
    /// FR-005. The aggregate throws and the handler translates; the guard is
    /// not duplicated here, because two copies of a rule are how spec 028's
    /// defect happened.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_is_refused_as_retired()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = await SeedAsync(cameras, "munich");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        Result<CameraIdentifier, ChangeCameraAddressError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { ExpectedVersion = camera.Version },
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ChangeCameraAddressError.CameraRetired>();
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);

        cameras.Cameras.Single().Url.Value.ShouldBe(OriginalUrl);
    }

    /// <summary>
    /// Idempotency as no <em>event</em>, not no error. Re-submitting the
    /// address the camera already has succeeds and announces nothing —
    /// announcing would add an audit row for a change that did not happen and
    /// would tell stream distribution to re-point a path that never moved,
    /// while the endpoint answered 204 either way.
    /// </summary>
    [Fact]
    public async Task Re_submitting_the_current_address_succeeds_and_announces_nothing()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = await SeedAsync(cameras, "munich");

        Result<CameraIdentifier, ChangeCameraAddressError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { Url = RtspUrl.From(OriginalUrl) },
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.Single().PendingEvents
            .OfType<CameraAddressChangedDomainEvent>()
            .ShouldBeEmpty();
    }

    private static ChangeCameraAddressCommand CommandFor(Camera camera, string fab) =>
        new(
            FabIdentifier.From(fab),
            camera.Id,
            RtspUrl.From(CorrectedUrl),
            camera.Version,
            AnAdmin);

    private static async Task<Camera> SeedAsync(InMemoryCameraRepository cameras, string fab)
    {
        Camera camera = Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From("line-3-inlet"),
            RtspUrl.From(OriginalUrl),
            AnAdmin,
            new FixedClock(FixedMoment));

        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);
        camera.ClearPendingEvents();

        return camera;
    }

    private static ChangeCameraAddressCommandHandler NewHandler(InMemoryCameraRepository cameras) =>
        new(cameras, new FixedClock(FixedMoment), NullLogger<ChangeCameraAddressCommandHandler>.Instance);
}
