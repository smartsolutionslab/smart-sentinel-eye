using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests;

/// <summary>
/// Spec 033 T009 — the uniqueness question on its own, before anything depends
/// on it.
///
/// <para>
/// This predicate carries the whole rule: per fab, case-insensitive, retired
/// cameras excluded, and now the subject of a rename excluded too. It has been
/// enforced inconsistently once already — spec 028 found the real repository
/// missing the <c>status &lt;&gt; 'Decommissioned'</c> filter the unique index
/// had always had, and every test stayed green because the double under test
/// was the correct one.
/// </para>
///
/// <para>
/// So these test the double directly. They are worth exactly as much as the
/// double's agreement with <c>CameraRepository</c>, which is why the two are
/// changed together and why the integration tests re-prove the same cases over
/// real SQL.
/// </para>
/// </summary>
public class ExistsByNameExclusionTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    /// <summary>
    /// The case the rename exists for. Without the exclusion the camera matches
    /// itself, and a rename is refused against its own name.
    /// </summary>
    [Fact]
    public async Task A_camera_does_not_count_as_holding_its_own_name()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", "line-3-inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-3-inlet"),
            Option<CameraIdentifier>.Some(camera.Id),
            CancellationToken.None);

        taken.ShouldBeFalse();
    }

    /// <summary>
    /// The case-only rename, which is the one a short-circuit on "new name
    /// equals current name" would still get wrong. Both spellings normalise
    /// identically, so the camera matches itself here too.
    /// </summary>
    [Fact]
    public async Task A_camera_does_not_count_when_only_the_letter_case_differs()
    {
        InMemoryCameraRepository cameras = new();
        Camera camera = Seed(cameras, "munich", "Line-3-Inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-3-inlet"),
            Option<CameraIdentifier>.Some(camera.Id),
            CancellationToken.None);

        taken.ShouldBeFalse();
    }

    /// <summary>
    /// The exclusion must not swallow everyone else — without this, the
    /// assertions above pass against a predicate that always answers false.
    /// </summary>
    [Fact]
    public async Task Another_active_camera_in_the_same_fab_does_count()
    {
        InMemoryCameraRepository cameras = new();
        Camera renaming = Seed(cameras, "munich", "line-3-inlet");
        Seed(cameras, "munich", "line-4-inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-4-inlet"),
            Option<CameraIdentifier>.Some(renaming.Id),
            CancellationToken.None);

        taken.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_camera_in_the_same_fab_counts_ignoring_letter_case()
    {
        InMemoryCameraRepository cameras = new();
        Camera renaming = Seed(cameras, "munich", "line-3-inlet");
        Seed(cameras, "munich", "Line-4-Inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-4-inlet"),
            Option<CameraIdentifier>.Some(renaming.Id),
            CancellationToken.None);

        taken.ShouldBeTrue();
    }

    /// <summary>Spec 028 FR-006 — retirement releases the name.</summary>
    [Fact]
    public async Task A_retired_camera_does_not_count()
    {
        InMemoryCameraRepository cameras = new();
        Camera renaming = Seed(cameras, "munich", "line-3-inlet");
        Camera gone = Seed(cameras, "munich", "line-4-inlet");
        gone.Retire(AnAdmin, new FixedClock(FixedMoment));

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-4-inlet"),
            Option<CameraIdentifier>.Some(renaming.Id),
            CancellationToken.None);

        taken.ShouldBeFalse();
    }

    /// <summary>Spec 015 — a name is unique within one fab, not across them.</summary>
    [Fact]
    public async Task A_camera_in_another_fab_does_not_count()
    {
        InMemoryCameraRepository cameras = new();
        Camera renaming = Seed(cameras, "munich", "line-3-inlet");
        Seed(cameras, "dresden", "line-4-inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("line-4-inlet"),
            Option<CameraIdentifier>.Some(renaming.Id),
            CancellationToken.None);

        taken.ShouldBeFalse();
    }

    /// <summary>
    /// Registration's question, unchanged. It excludes nothing because the
    /// camera does not exist yet, and this asserts the added parameter did not
    /// quietly alter the answer it has always given.
    /// </summary>
    [Fact]
    public async Task Excluding_nothing_answers_exactly_as_before()
    {
        InMemoryCameraRepository cameras = new();
        Seed(cameras, "munich", "line-3-inlet");

        bool taken = await cameras.ExistsByNameAsync(
            FabIdentifier.From("munich"),
            CameraName.From("LINE-3-INLET"),
            Option<CameraIdentifier>.None,
            CancellationToken.None);

        taken.ShouldBeTrue();
    }

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
}
