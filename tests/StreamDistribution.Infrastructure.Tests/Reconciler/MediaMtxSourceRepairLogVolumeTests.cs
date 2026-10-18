using System.Net;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2994 / spec 388. A path MediaMTX keeps refusing is the same fault on every
/// pass, so it Warns when it starts, when its outcome changes, and at most every
/// ten minutes while it persists; the passes in between log at Debug. One
/// <see cref="MediaMtxSourceRepair"/> instance serves every pass of a fact, as the
/// sweep's does. The passes run back to back, well inside the ten minutes.
/// </summary>
public class MediaMtxSourceRepairLogVolumeTests
{
    private const string FailureText = "Reconciler failed to repair MediaMTX path";

    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
    private static readonly MediaMtxPath PathB = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepairLogVolumeTests()
    {
        repair = new MediaMtxSourceRepair(logger);
    }

    [Fact]
    public async Task A_permanently_refused_patch_warns_once_across_three_passes_and_logs_debug_in_between()
    {
        Diverge(PathA);
        var refusal = Refusal(HttpStatusCode.BadRequest);
        gateway.FailPatchesFor(PathA, refusal);

        await Passes(3);

        var warnings = WarningsFor(PathA);
        warnings.Count.ShouldBe(1);
        warnings[0].Exception.ShouldBeSameAs(refusal);
        warnings[0].Message.ShouldBe($"{FailureText} {PathA}; the next sweep retries.");
        var debugs = logger.Entries
            .Where(entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains("HttpRequestException 400", StringComparison.Ordinal))
            .ToList();
        debugs.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_change_of_status_warns_again_at_once()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        var unauthorized = Refusal(HttpStatusCode.Unauthorized);
        gateway.FailPatchesFor(PathA, unauthorized);
        await Passes(2);

        var warnings = WarningsFor(PathA);
        warnings.Count.ShouldBe(2);
        warnings[1].Exception.ShouldBeSameAs(unauthorized);
    }

    [Fact]
    public async Task A_change_of_exception_type_warns_again_at_once()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        gateway.FailPatchesFor(PathA, new TimeoutRejectedException("The operation did not complete within the allowed timeout."));
        await Passes(2);

        var warnings = WarningsFor(PathA);
        warnings.Count.ShouldBe(2);
        warnings[1].Exception.ShouldBeOfType<TimeoutRejectedException>();
    }

    [Fact]
    public async Task A_repair_that_succeeds_resets_the_path()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        gateway.StopFailingPatchesFor(PathA);
        await Passes(1);
        gateway.Configured[PathA].ShouldBe("rtsp://10.0.8.1/h264", "the repair that succeeded wrote the database's value");

        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(2);

        WarningsFor(PathA).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_404_makes_no_confirmation_read()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));

        await Passes(1);

        snapshot.CommittedReads.ShouldBeEmpty("only a 404 is confirmed against the database");
    }

    [Fact]
    public async Task A_persistent_404_for_a_live_stream_warns_once_and_then_logs_debug_until_the_interval()
    {
        Diverge(PathA);
        var notFound = Refusal(HttpStatusCode.NotFound);
        gateway.FailPatchesFor(PathA, notFound);

        await Passes(3);

        var warnings = WarningsFor(PathA);
        warnings.Count.ShouldBe(1, "a 404 for a live stream is a Warning, not path gone");
        warnings[0].Exception.ShouldBeSameAs(notFound);
        warnings[0].Message.ShouldBe($"{FailureText} {PathA}; the next sweep retries.");
        var debugs = logger.Entries
            .Where(entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains("HttpRequestException 404", StringComparison.Ordinal))
            .ToList();
        debugs.Count.ShouldBeGreaterThanOrEqualTo(2);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
        snapshot.CommittedReads.ShouldBe([PathA, PathA, PathA], "one confirmation read per 404, one 404 per pass");
    }

    [Fact]
    public async Task A_404_for_a_live_stream_between_two_400s_is_a_change_of_outcome_each_time()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        var notFound = Refusal(HttpStatusCode.NotFound);
        gateway.FailPatchesFor(PathA, notFound);
        await Passes(1);

        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        var warnings = WarningsFor(PathA);
        warnings.Count.ShouldBe(3, "400 to 404 to 400 is three changes of outcome");
        warnings[1].Exception.ShouldBeSameAs(notFound);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_404_from_a_retire_race_is_path_gone_at_debug_never_a_warning()
    {
        Diverge(PathA);
        Diverge(PathB);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, path, _) =>
        {
            if (path == PathA)
            {
                snapshot.Retire(PathA);
            }
        };

        await Passes(1);

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        var gone = logger.Entries
            .Where(entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains("path gone", StringComparison.Ordinal))
            .ToList();
        gone.Count.ShouldBe(1);
        gateway.Configured[PathB].ShouldBe("rtsp://10.0.8.1/h264");
    }

    [Fact]
    public async Task A_path_pruned_after_leaving_the_database_warns_again_when_it_returns()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        snapshot.Retire(PathA);
        await Passes(1);

        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        await Passes(2);

        WarningsFor(PathA).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_cancelled_pass_leaves_the_state_untouched()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        using CancellationTokenSource shutdown = new();
        gateway.BeforePatchApplied = (_, _, _) => shutdown.Cancel();
        gateway.FailPatchesFor(PathA, new OperationCanceledException(shutdown.Token));
        await Should.ThrowAsync<OperationCanceledException>(
            () => repair.RepairAsync(gateway, snapshot, shutdown.Token));
        logger.Entries.Count(entry => entry.Level >= LogLevel.Warning).ShouldBe(1, "the cancelled pass logged nothing at Warning");

        gateway.BeforePatchApplied = null;
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        WarningsFor(PathA).Count.ShouldBe(1);
    }

    [Fact]
    public async Task No_debug_line_carries_the_source()
    {
        snapshot.Commit(PathA, "rtsp://cam/s?token=SECRET");
        gateway.Configure(PathA, "rtsp://cam/other");
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.BadRequest));

        await Passes(3);

        logger.Entries.ShouldNotBeEmpty();
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal));
    }

    private static HttpRequestException Refusal(HttpStatusCode status) =>
        new($"Response status code does not indicate success: {(int)status} ({status}).", null, status);

    private void Diverge(MediaMtxPath path)
    {
        snapshot.Commit(path, "rtsp://10.0.8.1/h264");
        gateway.Configure(path, "rtsp://10.0.8.2/h264");
    }

    private List<(LogLevel Level, string Message, Exception? Exception)> WarningsFor(MediaMtxPath path) =>
        [.. logger.Entries.Where(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(path.ToString(), StringComparison.Ordinal))];

    private async Task Passes(int count)
    {
        for (var pass = 0; pass < count; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }
    }
}
