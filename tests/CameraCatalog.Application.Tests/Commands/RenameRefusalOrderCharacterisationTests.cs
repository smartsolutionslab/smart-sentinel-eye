using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 353, ADR-0144 phase 4a: behaviour-preserving, so green
/// on the unrefactored handler and green unmodified after). Pins the order in
/// which <see cref="RenameCameraCommandHandler"/> refuses, the repository calls
/// it makes on the way, what it excludes from the name lookup, and its log text.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the stale-version check
/// below the name-taken check turns "stale and name taken" from
/// CAMERA_VERSION_STALE (one repository call) into CAMERA_NAME_TAKEN (two).
/// </para>
///
/// <para>
/// Two oddities are pinned as they are, not asserted as desirable (spec 353
/// section 6). A retired camera renamed onto a name another active camera holds
/// answers CAMERA_NAME_TAKEN, not CAMERA_RETIRED, because the uniqueness check
/// runs before the aggregate's terminal rule. And CAMERA_RETIRED is the only
/// rename refusal that logs nothing.
/// </para>
///
/// <para>
/// Every expected message is a hand-written template in which only a fixture
/// identifier is interpolated, never built with the <c>RenameCameraFailures</c>
/// factories under test. The registered version is 0 and the stale case quotes 7,
/// so a transposition of the two in a message or a log line fails.
/// </para>
/// </summary>
public class RenameRefusalOrderCharacterisationTests
{
    private const int StaleExpectedVersion = 7;
    private const string TargetName = "line-4-inlet";

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("11111111-2222-7333-8444-555555555555"));

    private static readonly Guid UnknownCamera = Guid.Parse("5e0f0c3a-1b2c-7d3e-8f40-516273849a0b");

    private readonly InMemoryCameraRepository store = new();
    private readonly RecordingCameraRepository repository;
    private readonly CapturingLogger<RenameCameraCommandHandler> logger = new();

    public RenameRefusalOrderCharacterisationTests()
    {
        repository = new RecordingCameraRepository(store);
    }

    [Fact]
    public async Task An_unknown_camera_is_not_found_after_one_fab_scoped_lookup_and_one_warning()
    {
        Seed("munich", "Line-4-Inlet");

        var result = await Rename(UnknownCamera, "munich", TargetName, StaleExpectedVersion);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
        result.Error.Message.ShouldBe("No camera with identifier '5e0f0c3a-1b2c-7d3e-8f40-516273849a0b' exists.");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        AssertCalls("get-within-fab");
        AssertSingleLog(
            LogLevel.Warning,
            "Rejected rename for camera 5e0f0c3a-1b2c-7d3e-8f40-516273849a0b: not found in the caller's fab.");
    }

    [Fact]
    public async Task A_stale_version_and_a_taken_name_answers_stale_and_never_looks_the_name_up()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        Seed("munich", "Line-4-Inlet");

        var result = await Rename(camera.Id.Value, "munich", TargetName, StaleExpectedVersion);

        AssertStale(result, camera);
        AssertCalls("get-within-fab");
        AssertStaleLog(camera);
    }

    [Fact]
    public async Task A_stale_version_and_a_retired_camera_answers_stale()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        var result = await Rename(camera.Id.Value, "munich", TargetName, StaleExpectedVersion);

        AssertStale(result, camera);
        AssertCalls("get-within-fab");
        AssertStaleLog(camera);
    }

    [Fact]
    public async Task A_stale_version_and_a_retired_camera_and_a_taken_name_answers_stale()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        Seed("munich", "Line-4-Inlet");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        var result = await Rename(camera.Id.Value, "munich", TargetName, StaleExpectedVersion);

        AssertStale(result, camera);
        AssertCalls("get-within-fab");
        AssertStaleLog(camera);
    }

    [Fact]
    public async Task A_stale_version_alone_answers_stale_after_one_lookup_and_does_not_save()
    {
        var camera = Seed("munich", "Line-3-Inlet");

        var result = await Rename(camera.Id.Value, "munich", TargetName, StaleExpectedVersion);

        AssertStale(result, camera);
        AssertCalls("get-within-fab");
        AssertStaleLog(camera);
        store.Cameras.Single().Name.Value.ShouldBe("Line-3-Inlet");
    }

    [Fact]
    public async Task A_taken_name_alone_is_refused_after_one_name_lookup_excluding_the_camera_itself()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        Seed("munich", "Line-4-Inlet");

        var result = await Rename(camera.Id.Value, "munich", TargetName, 0);

        AssertTaken(result);
        AssertCalls("get-within-fab", "exists-by-name");
        AssertNameLookup(camera);
        AssertTakenLog(camera);
        store.Cameras.First(candidate => candidate.Id.Equals(camera.Id)).Name.Value.ShouldBe("Line-3-Inlet");
    }

    [Fact]
    public async Task A_retired_camera_renamed_onto_a_name_another_active_camera_holds_answers_taken_not_retired()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        Seed("munich", "Line-4-Inlet");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        var result = await Rename(camera.Id.Value, "munich", TargetName, 0);

        AssertTaken(result);
        AssertCalls("get-within-fab", "exists-by-name");
        AssertNameLookup(camera);
        AssertTakenLog(camera);
    }

    [Fact]
    public async Task A_retired_camera_with_a_free_name_answers_retired_after_the_name_lookup_logs_nothing_and_does_not_save()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        var result = await Rename(camera.Id.Value, "munich", TargetName, 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_RETIRED");
        result.Error.Message.ShouldBe($"Camera '{camera.Id.Value}' is retired; it cannot be renamed.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        AssertCalls("get-within-fab", "exists-by-name");
        AssertNameLookup(camera);
        logger.Entries.ShouldBeEmpty();
        store.Cameras.Single().Name.Value.ShouldBe("Line-3-Inlet");
    }

    [Fact]
    public async Task A_free_name_saves_once_and_logs_the_rename_at_information()
    {
        var camera = Seed("munich", "Line-3-Inlet");
        Seed("munich", "Line-9-Outlet");

        var result = await Rename(camera.Id.Value, "munich", TargetName, 0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(camera.Id);
        AssertCalls("get-within-fab", "exists-by-name", "save");
        AssertNameLookup(camera);
        AssertSingleLog(LogLevel.Information, $"Renamed camera {camera.Id.Value} to line-4-inlet.");
        store.Cameras.First(candidate => candidate.Id.Equals(camera.Id)).Name.Value.ShouldBe(TargetName);
    }

    [Fact]
    public async Task A_case_only_rename_succeeds_because_the_lookup_excludes_the_camera_itself()
    {
        var camera = Seed("munich", "Line-4-Inlet");

        var result = await Rename(camera.Id.Value, "munich", TargetName, 0);

        result.IsSuccess.ShouldBeTrue();
        AssertCalls("get-within-fab", "exists-by-name", "save");
        AssertNameLookup(camera);
        AssertSingleLog(LogLevel.Information, $"Renamed camera {camera.Id.Value} to line-4-inlet.");
        store.Cameras.Single().Name.Value.ShouldBe(TargetName);
    }

    private static void AssertStale(Result<CameraIdentifier, RenameCameraError> result, Camera camera)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_VERSION_STALE");
        result.Error.Message.ShouldBe(
            $"Camera '{camera.Id.Value}' is at version 0, not 7. Re-read it before reapplying your change.");
        result.Error.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    private static void AssertTaken(Result<CameraIdentifier, RenameCameraError> result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NAME_TAKEN");
        result.Error.Message.ShouldBe(
            "Another camera in fab 'munich' is already called 'line-4-inlet'. Names are unique per fab, ignoring case.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private Camera Seed(string fab, string name)
    {
        var camera = Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.7.12/h264"),
            AnAdmin,
            new FixedClock(FixedMoment));

        store.Add(camera);
        store.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
        camera.ClearPendingEvents();

        return camera;
    }

    private void AssertStaleLog(Camera camera) =>
        AssertSingleLog(
            LogLevel.Warning,
            $"Rejected rename for camera {camera.Id.Value}: expected version 7, actual 0.");

    private void AssertTakenLog(Camera camera) =>
        AssertSingleLog(
            LogLevel.Warning,
            $"Rejected rename for camera {camera.Id.Value}: line-4-inlet is already taken in this fab.");

    private void AssertSingleLog(LogLevel level, string message)
    {
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(level);
        entry.Message.ShouldBe(message);
    }

    private void AssertCalls(params string[] expected) => repository.Calls.ShouldBe(expected);

    private void AssertNameLookup(Camera camera)
    {
        var lookup = repository.NameLookups.ShouldHaveSingleItem();
        lookup.Fab.Value.ShouldBe("munich");
        lookup.Name.Value.ShouldBe(TargetName);
        lookup.Excluding.HasValue.ShouldBeTrue();
        lookup.Excluding.Value.Value.ShouldBe(camera.Id.Value);
    }

    private async Task<Result<CameraIdentifier, RenameCameraError>> Rename(
        Guid camera,
        string fab,
        string name,
        int expectedVersion)
    {
        RenameCameraCommandHandler handler = new(repository, new FixedClock(FixedMoment), logger);

        return await handler.HandleAsync(
            new RenameCameraCommand(
                FabIdentifier.From(fab),
                CameraIdentifier.From(camera),
                CameraName.From(name),
                expectedVersion,
                AnAdmin),
            CancellationToken.None);
    }
}
