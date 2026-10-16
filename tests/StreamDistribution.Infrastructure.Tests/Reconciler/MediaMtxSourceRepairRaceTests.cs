using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378 plan §3. A repoint is "commit B, then PATCH B". Each fact
/// drives one interleaving of that against the pass by running the repoint
/// from a hook at the exact point named. Every fact also asserts the invariant
/// that the pass only ever wrote a value the database handed it.
/// </summary>
public class MediaMtxSourceRepairRaceTests
{
    private const string Stale = "rtsp://10.0.0.1/stale";
    private const string Old = "rtsp://10.0.0.2/old";
    private const string Newer = "rtsp://10.0.0.3/newer";

    private static readonly MediaMtxPath Path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();

    [Fact]
    public async Task A_repoint_between_the_mediamtx_read_and_the_database_read_writes_the_newer_value()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        gateway.AfterListCaptured = _ =>
        {
            snapshot.Commit(Path, Newer);
            gateway.Configure(Path, Newer);
        };

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Configured[Path].ShouldBe(Newer);
        gateway.Patches.ShouldNotContain(patch => patch.Source == Old, "the stale value was never read from the database");
        OnlyDatabaseValuesWereWritten();
    }

    [Fact]
    public async Task A_repoint_whose_patch_lands_before_ours_is_overwritten_by_the_re_read()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        gateway.BeforePatchApplied = (_, _, source) =>
        {
            if (source == Old)
            {
                snapshot.Commit(Path, Newer);
                gateway.Configure(Path, Newer);
                gateway.BeforePatchApplied = null;
            }
        };

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.Select(patch => patch.Source).ShouldBe([Old, Newer]);
        gateway.Configured[Path].ShouldBe(Newer);
        OnlyDatabaseValuesWereWritten();
    }

    [Fact]
    public async Task A_repoint_that_commits_during_our_patch_and_patches_after_ends_on_the_newer_value()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        gateway.AfterPatchApplied = (_, _, source) =>
        {
            if (source == Old)
            {
                snapshot.Commit(Path, Newer);
                gateway.AfterPatchApplied = null;
            }
        };

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        await gateway.RepointPathAsync(Path, Newer, CancellationToken.None);

        gateway.Configured[Path].ShouldBe(snapshot.Committed(Path).Value);
        gateway.Patches.Count(patch => patch.Source != Newer || patch.Path != Path).ShouldBeLessThanOrEqualTo(1);
        OnlyDatabaseValuesWereWritten();
    }

    [Fact]
    public async Task A_repoint_that_commits_after_our_re_read_patches_last_and_wins()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        snapshot.AfterCommittedReadCaptured = (_, _) =>
        {
            snapshot.Commit(Path, Newer);
            snapshot.AfterCommittedReadCaptured = null;
        };

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        gateway.Patches.Select(patch => patch.Source).ShouldBe([Old], "the pass must not chase a commit it never saw");

        await gateway.RepointPathAsync(Path, Newer, CancellationToken.None);

        gateway.Configured[Path].ShouldBe(snapshot.Committed(Path).Value);
    }

    [Fact]
    public async Task A_stale_retry_that_lands_after_a_converged_pass_is_repaired_by_the_next_pass()
    {
        snapshot.Commit(Path, Newer);
        gateway.Configure(Path, Newer);

        var converged = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        gateway.Configure(Path, Old);
        var repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        (converged, repaired).ShouldBe((0, 1));
        gateway.Configured[Path].ShouldBe(Newer);
        OnlyDatabaseValuesWereWritten();
    }

    [Fact]
    public async Task A_path_never_receives_more_than_two_patches_in_one_pass_however_often_the_source_changes()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        snapshot.CommittedReadOverride = (_, read) =>
            Option<StreamSourceUrl>.Some(StreamSourceUrl.From($"rtsp://10.0.9.{read}/churn"));

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.Count.ShouldBe(2);
        snapshot.CommittedReads.Count.ShouldBeLessThanOrEqualTo(1, "the pass re-reads once and never loops");
        OnlyDatabaseValuesWereWritten();
    }

    [Fact]
    public async Task A_retire_committed_during_the_pass_stops_the_re_patch()
    {
        snapshot.Commit(Path, Old);
        gateway.Configure(Path, Stale);
        gateway.AfterPatchApplied = (_, _, _) => snapshot.Retire(Path);

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.Select(patch => patch.Source).ShouldBe([Old]);
        gateway.Added.ShouldBeEmpty();
        gateway.Removed.ShouldBeEmpty();
    }

    private void OnlyDatabaseValuesWereWritten()
    {
        var served = snapshot.Served.Select(source => source.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var (_, source) in gateway.Patches)
        {
            served.ShouldContain(source, "every PATCH value must be one the database handed the pass (FR-004)");
        }
    }

    private MediaMtxSourceRepair Pass() => new(logger);
}
