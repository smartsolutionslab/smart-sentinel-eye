using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378, adversarial pass. The interleavings and inputs the
/// happy-path and race facts leave out: a retire or a provision landing inside
/// the pass, a cancellation mid-pass, and source values MediaMTX may hold that
/// the database never would.
/// </summary>
public class MediaMtxSourceRepairAdversarialTests
{
    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
    private static readonly MediaMtxPath PathB = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();

    [Fact]
    public async Task A_retire_that_removes_the_path_before_our_patch_is_skipped_and_the_pass_continues()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        snapshot.Commit(PathB, "rtsp://10.0.8.5/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.Configure(PathB, "rtsp://10.0.8.6/h264");
        gateway.FailPatchesFor(
            PathA,
            new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, path, _) =>
        {
            if (path == PathA)
            {
                snapshot.Retire(PathA);
            }
        };

        var repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1);
        gateway.Configured[PathB].ShouldBe("rtsp://10.0.8.5/h264");
        gateway.Added.ShouldBeEmpty("a PATCH answered 404 must never turn into a re-add of a retired stream");
        gateway.Removed.ShouldBeEmpty();
        snapshot.CommittedReads.ShouldNotContain(PathA, "a refused patch is not followed by a re-read");
    }

    [Fact]
    public async Task A_stream_provisioned_after_the_mediamtx_read_is_left_to_the_add_path()
    {
        var provisioned = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        gateway.AfterListCaptured = _ =>
        {
            snapshot.Commit(provisioned, "rtsp://10.0.8.7/h264");
            gateway.Configure(provisioned, "rtsp://10.0.8.7/h264");
        };

        var repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(0);
        gateway.Patches.ShouldBeEmpty("a path MediaMTX did not report is the add/ path's, not the repair's");
        gateway.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_orphan_whose_stream_commits_between_the_two_reads_is_pointed_at_the_new_rows_source()
    {
        // add/'s 400 branch accepts an existing path without checking its source
        // (spec 378 §1), so this is the provisioning entry to the divergence.
        var path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        gateway.Configure(path, "rtsp://10.0.9.9/leftover");
        gateway.AfterListCaptured = _ => snapshot.Commit(path, "rtsp://10.0.8.8/h264");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.ShouldBe([(path, "rtsp://10.0.8.8/h264")]);
        gateway.Removed.ShouldBeEmpty("orphan removal stays the startup pass's");
    }

    [Fact]
    public async Task A_cancellation_mid_pass_propagates_and_patches_nothing_further()
    {
        using CancellationTokenSource shutdown = new();
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        snapshot.Commit(PathB, "rtsp://10.0.8.5/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.Configure(PathB, "rtsp://10.0.8.6/h264");
        gateway.BeforePatchApplied = (_, _, _) => shutdown.Cancel();
        gateway.FailPatchesFor(PathA, new OperationCanceledException(shutdown.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            () => Pass().RepairAsync(gateway, snapshot, shutdown.Token));

        gateway.Patches.Select(patch => patch.Path).ShouldBe([PathA]);
        logger.Entries.ShouldNotContain(
            entry => entry.Level >= LogLevel.Warning,
            "a shutdown is not a failed repair");
    }

    [Theory]
    [InlineData("publisher", "<unparseable url>")]
    [InlineData("rtsp://admin:SECRET@cam/x?token=SECRET", "rtsp://cam")]
    [InlineData("", "<unparseable url>")]
    [InlineData("rtsp://cam/SECRET%2Fstill-secret", "rtsp://cam")]
    public async Task A_source_only_mediamtx_holds_is_logged_by_origin_and_never_verbatim(
        string observed,
        string renderedAs)
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, observed);

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Configured[PathA].ShouldBe("rtsp://10.0.8.1/h264");
        logger.Entries.ShouldContain(
            entry => entry.Message == $"Reconciler repaired MediaMTX path {PathA} source {renderedAs} -> rtsp://10.0.8.1.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_source_that_keeps_moving_never_gets_more_than_two_patches_even_when_the_last_re_read_is_retired()
    {
        snapshot.Commit(PathA, "rtsp://10.0.0.2/old");
        gateway.Configure(PathA, "rtsp://10.0.0.1/stale");
        snapshot.CommittedReadOverride = (_, read) => read == 1
            ? Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://10.0.0.3/newer"))
            : Option<StreamSourceUrl>.None;

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.Count.ShouldBe(2);
        snapshot.CommittedReads.Count.ShouldBe(1, "the second patch is final; it is not followed by another re-read");
    }

    private MediaMtxSourceRepair Pass() => new(logger);
}
