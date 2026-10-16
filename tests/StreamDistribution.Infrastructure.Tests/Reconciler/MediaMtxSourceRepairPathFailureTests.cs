using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378, second adversarial pass. One path's failure, whatever its
/// type, costs that path its repair this pass and nothing more; only the
/// caller's own cancellation ends the pass.
/// </summary>
public class MediaMtxSourceRepairPathFailureTests
{
    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
    private static readonly MediaMtxPath PathB = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();

    public static TheoryData<Exception> NonHttpPatchFailures =>
    [
        new TimeoutRejectedException("The operation did not complete within the allowed timeout of '00:00:30'."),
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."),
    ];

    [Theory]
    [MemberData(nameof(NonHttpPatchFailures))]
    public async Task A_patch_that_fails_with_a_non_http_exception_does_not_stop_the_other_path_being_repaired(
        Exception failure)
    {
        Diverge();
        gateway.FailPatchesFor(PathA, failure);

        var repaired = 0;
        await Should.NotThrowAsync(async () => repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None));

        repaired.ShouldBe(1);
        gateway.Configured[PathB].ShouldBe("rtsp://10.0.8.5/h264");
        AssertOneWarningFor(PathA, failure);
    }

    [Fact]
    public async Task A_committed_source_re_read_that_fails_does_not_stop_the_other_path_being_repaired()
    {
        Diverge();
        InvalidOperationException failure = new("connection reset by peer");
        snapshot.CommittedReadOverride = (path, _) => path == PathA
            ? throw failure
            : Option<StreamSourceUrl>.Some(snapshot.Committed(path));

        var repaired = 0;
        await Should.NotThrowAsync(async () => repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None));

        repaired.ShouldBeGreaterThanOrEqualTo(1);
        gateway.Configured[PathB].ShouldBe("rtsp://10.0.8.5/h264");
        AssertOneWarningFor(PathA, failure);
    }

    [Fact]
    public async Task A_cancellation_of_the_callers_token_that_surfaces_as_a_task_cancelled_exception_still_ends_the_pass()
    {
        using CancellationTokenSource shutdown = new();
        Diverge();
        gateway.BeforePatchApplied = (_, _, _) => shutdown.Cancel();
        gateway.FailPatchesFor(PathA, new TaskCanceledException("canceled", null, shutdown.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            () => Pass().RepairAsync(gateway, snapshot, shutdown.Token));

        gateway.Patches.Select(patch => patch.Path).ShouldBe([PathA], "nothing is patched after the caller cancelled");
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private void Diverge()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        snapshot.Commit(PathB, "rtsp://10.0.8.5/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.Configure(PathB, "rtsp://10.0.8.6/h264");
    }

    private void AssertOneWarningFor(MediaMtxPath path, Exception failure)
    {
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();

        warnings.Count.ShouldBe(1, "one Warning per failed path");
        warnings[0].Message.ShouldContain(path.ToString());
        warnings[0].Exception.ShouldBeSameAs(failure);
        warnings[0].Message.ShouldNotContain("h264", Case.Insensitive, "a full URL never reaches the log; the origin does");
    }

    private MediaMtxSourceRepair Pass() => new(logger);
}
