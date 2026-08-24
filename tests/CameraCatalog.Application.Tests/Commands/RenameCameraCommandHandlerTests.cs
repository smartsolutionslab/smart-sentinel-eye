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
/// Spec 033 T014–T018 — correcting a camera's name.
/// </summary>
public class RenameCameraCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalName = "line-3-inlet";
    private const string CorrectedName = "line-4-inlet";

    [Fact]
    public async Task Renaming_stores_the_new_name_and_keeps_the_identifier()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        CameraIdentifier before = camera.Id;

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.Single().Name.Value.ShouldBe(CorrectedName);

        // SC-001. The whole point: retire-and-re-register already changes the
        // name, and produces a different identifier.
        cameras.Cameras.Single().Id.ShouldBe(before);
    }

    /// <summary>
    /// <b>T014, half one.</b> FR-010 — idempotency as no <em>event</em>.
    /// </summary>
    [Fact]
    public async Task Renaming_to_the_name_it_already_has_succeeds_and_announces_nothing()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { Name = CameraName.From(OriginalName) },
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.Single().PendingEvents
            .OfType<CameraRenamedDomainEvent>()
            .ShouldBeEmpty();
    }

    /// <summary>
    /// <b>T014, half two — and the half that catches the wrong fix.</b>
    ///
    /// <para>
    /// A case-only correction is a <em>real</em> change: what an operator reads
    /// on a wall of live video changes. But it normalises identically, so it
    /// hits both traps at once — the uniqueness check finds the camera matching
    /// itself, and <c>CameraName.Equals</c> (which compares NormalizedValue)
    /// reports the name as unchanged.
    /// </para>
    ///
    /// <para>
    /// This test exists in the same task as the one above precisely because the
    /// tempting fix — short-circuiting when the new name equals the current one
    /// — makes that one pass and leaves this one failing. Deleting this test
    /// makes the wrong implementation look correct.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Renaming_only_the_letter_case_succeeds_and_is_stored()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", "Line-3-Inlet");

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { Name = CameraName.From("line-3-inlet") },
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        // Stored, not silently discarded as "the same name".
        cameras.Cameras.Single().Name.Value.ShouldBe("line-3-inlet");

        // And announced, because something did change.
        cameras.Cameras.Single().PendingEvents
            .OfType<CameraRenamedDomainEvent>()
            .ShouldHaveSingleItem();
    }

    /// <summary>T015 — FR-006, the collision that must fire.</summary>
    [Fact]
    public async Task Renaming_onto_another_active_cameras_name_in_the_same_fab_is_refused()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        Seed(cameras, "munich", CorrectedName);

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RenameCameraError.NameTaken>();

        cameras.Cameras.First(c => c.Id.Equals(camera.Id)).Name.Value.ShouldBe(OriginalName);
    }

    /// <summary>
    /// T015. Asserting only the exact match above passes against a
    /// case-sensitive comparison — which is precisely defect #1434, found in
    /// this same predicate.
    /// </summary>
    [Fact]
    public async Task Renaming_onto_a_name_differing_only_in_case_is_refused()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        Seed(cameras, "munich", "LINE-4-INLET");

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RenameCameraError.NameTaken>();
    }

    /// <summary>
    /// T016 — a refusal that must <em>not</em> fire. Spec 015: a name is unique
    /// within one fab, not across them.
    /// </summary>
    [Fact]
    public async Task A_camera_in_another_fab_holding_the_name_does_not_block_the_rename()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        Seed(cameras, "dresden", CorrectedName);

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.First(c => c.Id.Equals(camera.Id)).Name.Value.ShouldBe(CorrectedName);
    }

    /// <summary>
    /// T016 — the other refusal that must not fire. Spec 028 FR-006:
    /// retirement releases the name for reuse within the fab.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_holding_the_name_does_not_block_the_rename()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        Camera gone = Seed(cameras, "munich", CorrectedName);
        gone.Retire(AnAdmin, new FixedClock(FixedMoment));

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        cameras.Cameras.First(c => c.Id.Equals(camera.Id)).Name.Value.ShouldBe(CorrectedName);
    }

    /// <summary>
    /// <b>T017 — the two conflicts, asserted apart.</b>
    ///
    /// <para>
    /// Both are conflicts and only one is fixable by re-reading. ADR-0119 makes
    /// the <em>code</em> what a caller keys on, and spec 031's architecture
    /// test enforces the <c>_STALE</c> suffix — but it says nothing about the
    /// two sharing a status, so that is asserted here. A caller keying on
    /// status would re-read and retry forever against a name that belongs to
    /// somebody else.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_taken_name_and_a_stale_version_are_never_the_same_refusal()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        Seed(cameras, "munich", CorrectedName);

        RenameCameraError taken = (await NewHandler(cameras).HandleAsync(
            CommandFor(camera, "munich"), CancellationToken.None)).Error;

        RenameCameraError stale = (await NewHandler(cameras).HandleAsync(
            CommandFor(camera, "munich") with { ExpectedVersion = camera.Version + 7 },
            CancellationToken.None)).Error;

        taken.Code.ShouldBe("CAMERA_NAME_TAKEN");
        stale.Code.ShouldBe("CAMERA_VERSION_STALE");
        taken.Code.ShouldNotBe(stale.Code);

        // ADR-0119: the suffix identifies a lost update. A taken name is not
        // one — the caller's version is fine and re-reading shows them exactly
        // what they already had.
        taken.Code.ShouldNotEndWith("_STALE", Case.Sensitive);
        stale.Code.ShouldEndWith("_STALE", Case.Sensitive);

        // The part the architecture test cannot see.
        taken.Status.ShouldNotBe(stale.Status);
        taken.Status.ShouldBe(HttpStatusCode.Conflict);
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);

        // Neither may tell the operator to simply try again: one needs a
        // re-read, the other needs a different name.
        taken.Message.ShouldNotContain("try again", Case.Insensitive);
        stale.Message.ShouldNotContain("try again", Case.Insensitive);
    }

    /// <summary>
    /// T018 — FR-009. The aggregate throws and the handler translates; the
    /// guard is not duplicated, because two copies of a rule is how spec 028's
    /// defect happened.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_cannot_be_renamed()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", OriginalName);
        camera.Retire(AnAdmin, new FixedClock(FixedMoment));

        Result<CameraIdentifier, RenameCameraError> result =
            await NewHandler(cameras).HandleAsync(
                CommandFor(camera, "munich") with { ExpectedVersion = camera.Version },
                CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RenameCameraError.CameraRetired>();
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);

        cameras.Cameras.Single().Name.Value.ShouldBe(OriginalName);
    }

    /// <summary>
    /// Spec 029 FR-006, restated because a rename adds three new ways to answer
    /// something more specific than "no such camera" — and each of them would
    /// confirm the camera exists.
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is()
    {
        InMemoryCameraRepository cameras = new();
        Camera inMunich = Seed(cameras, "munich", OriginalName);

        Result<CameraIdentifier, RenameCameraError> crossFab =
            await NewHandler(cameras).HandleAsync(CommandFor(inMunich, "dresden"), CancellationToken.None);

        crossFab.IsFailure.ShouldBeTrue();
        crossFab.Error.ShouldBeOfType<RenameCameraError.CameraNotFound>();
        crossFab.Error.Status.ShouldBe(HttpStatusCode.NotFound);

        cameras.Cameras.Single().Name.Value.ShouldBe(OriginalName);
    }

    [Fact]
    public async Task An_unknown_camera_is_not_found()
    {
        InMemoryCameraRepository cameras = new();

        Result<CameraIdentifier, RenameCameraError> result = await NewHandler(cameras).HandleAsync(
            new RenameCameraCommand(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                CameraName.From(CorrectedName),
                ExpectedVersion: 0,
                AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
    }

    /// <summary>
    /// FR-013. The rename appends to history; it does not revisit it. A camera
    /// renamed after registration keeps a registration event naming what it was
    /// called then.
    /// </summary>
    [Fact]
    public async Task Renaming_does_not_rewrite_what_the_camera_was_called_before()
    {
        InMemoryCameraRepository cameras = new();

        Camera camera = Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From(OriginalName),
            RtspUrl.From("rtsp://10.0.7.12/h264"),
            AnAdmin,
            new FixedClock(FixedMoment));
        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);

        await NewHandler(cameras).HandleAsync(CommandFor(camera, "munich"), CancellationToken.None);

        CameraRegisteredDomainEvent registered = camera.PendingEvents
            .OfType<CameraRegisteredDomainEvent>()
            .Single();

        registered.Name.Value.ShouldBe(OriginalName);

        CameraRenamedDomainEvent renamed = camera.PendingEvents
            .OfType<CameraRenamedDomainEvent>()
            .Single();

        renamed.PreviousName.Value.ShouldBe(OriginalName);
        renamed.Name.Value.ShouldBe(CorrectedName);
    }

    private static RenameCameraCommand CommandFor(Camera camera, string fab) =>
        new(
            FabIdentifier.From(fab),
            camera.Id,
            CameraName.From(CorrectedName),
            camera.Version,
            AnAdmin);

    private static Camera Seed(InMemoryCameraRepository cameras, string fab, string name)
    {
        Camera camera = Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.7.12/h264"),
            AnAdmin,
            new FixedClock(FixedMoment));

        cameras.Add(camera);
        cameras.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
        camera.ClearPendingEvents();

        return camera;
    }

    private static RenameCameraCommandHandler NewHandler(InMemoryCameraRepository cameras) =>
        new(cameras, new FixedClock(FixedMoment), NullLogger<RenameCameraCommandHandler>.Instance);
}
