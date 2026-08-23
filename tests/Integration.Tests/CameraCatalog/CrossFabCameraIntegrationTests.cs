using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using CameraAggregate = SmartSentinelEye.CameraCatalog.Domain.Camera.Camera;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 015 T014 — fab scoping of the camera catalogue, against the real stack.
/// Covers SC-001.
///
/// <para>
/// The handler tests prove the duplicate-name check consults the fab, but they
/// run against an in-memory double the test itself populates. This exercises
/// what they stub: the real migration's <c>fab</c> column, its backfill, and
/// the <c>(fab, name)</c> unique index — the last of which is the only thing
/// that can prove the migration <em>swapped</em> the index rather than adding
/// a column beside it.
/// </para>
///
/// <para>
/// Cameras are seeded through a <c>DbContext</c> rather than the HTTP API: the
/// seeded admin belongs to <c>/fabs/munich</c> only, so registering a dresden
/// camera over HTTP is refused — which is the behaviour under test rather than
/// a way to set it up.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabCameraIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Before spec 015 the unique index was on <c>name</c> alone, so the second
    /// insert here failed outright. That it now succeeds is the index swap
    /// observed rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_same_camera_name_is_accepted_in_two_fabs()
    {
        string name = UniqueName();
        await SeedAsync("munich", name);
        await SeedAsync("dresden", name);

        await using CameraCatalogDbContext context = await aspire.CreateCameraCatalogDbContextAsync();
        CameraName parsed = CameraName.From(name);

        List<CameraAggregate> stored = await context.Cameras
            .Where(camera => camera.Name == parsed)
            .ToListAsync();

        stored.Count.ShouldBe(2);
        stored.Select(camera => camera.Fab.Value).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// The other half: within one fab the name is still unique, so the index
    /// swap widened the key rather than dropping the constraint.
    /// </summary>
    [Fact]
    public async Task The_same_camera_name_is_still_refused_within_one_fab()
    {
        string name = UniqueName();
        await SeedAsync("munich", name);

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync("munich", name));
    }

    /// <summary>
    /// Spec 015 T015. Skipped from the day it was written until #1434 was
    /// fixed: the index was a plain btree on the raw <c>name</c> column despite
    /// a name and a comment both claiming <c>lower()</c>, and
    /// <c>ExistsByNameAsync</c> compared that same column, so neither layer
    /// caught it. <c>CameraName.Equals</c> is case-insensitive but never ran,
    /// because EF translated the predicate to SQL.
    ///
    /// <para>
    /// It seeds through a <c>DbContext</c> rather than the API deliberately —
    /// that bypasses the handler, so what is under test is the <b>database</b>
    /// constraint, not the application check. Both were wrong; this is the half
    /// that has to hold when the other is bypassed.
    /// </para>
    ///
    /// <para>
    /// Kept skipped rather than deleted or weakened, which is why the evidence
    /// survived long enough to fix: a permanently red test gets ignored and
    /// then removed, and one weakened to match the defect has to be found and
    /// reversed later.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_name_differing_only_in_case_is_refused_within_one_fab()
    {
        string name = UniqueName();
        await SeedAsync("munich", name.ToUpperInvariant());

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync("munich", name.ToLowerInvariant()));
    }

    /// <summary>
    /// Passes today, but for the wrong reason: nothing enforces
    /// case-insensitivity at all (#1434), so of course the cross-fab case is
    /// accepted. Kept because it must still hold <em>after</em> #1434 is fixed
    /// — that is when it starts being a real assertion rather than a
    /// coincidence.
    /// </summary>
    [Fact]
    public async Task A_name_differing_only_in_case_is_accepted_in_another_fab()
    {
        // Case-insensitivity, once enforced, must be scoped to the fab like
        // everything else — it must not leak across plants and re-create the
        // collision spec 015 removes.
        string name = UniqueName();
        await SeedAsync("munich", name.ToUpperInvariant());

        await SeedAsync("dresden", name.ToLowerInvariant());

        await using CameraCatalogDbContext context = await aspire.CreateCameraCatalogDbContextAsync();
        (await context.Cameras.CountAsync()).ShouldBe(2);
    }

    private async Task SeedAsync(string fab, string name)
    {
        await using CameraCatalogDbContext context = await aspire.CreateCameraCatalogDbContextAsync();

        CameraAggregate camera = CameraAggregate.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From($"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new SystemClock());
        camera.ClearPendingEvents();

        context.Cameras.Add(camera);
        await context.SaveChangesAsync();
    }

    private static string UniqueName() => $"Cam-{Guid.NewGuid():N}"[..12];
}
