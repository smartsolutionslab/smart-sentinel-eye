using System.Net;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2994 / spec 388, adversarial pass (T006): outage and flood. One
/// <see cref="MediaMtxSourceRepair"/> instance serves every pass of a fact, as the
/// sweep's does, and the passes run back to back, far inside the ten-minute
/// repeat interval.
/// </summary>
public class MediaMtxSourceRepairFloodAdversarialTests
{
    private const string FailureText = "Reconciler failed to repair MediaMTX path";
    private const string Persisted = "rtsp://10.0.8.1/h264";
    private const string Drifted = "rtsp://10.0.8.2/h264";

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepairFloodAdversarialTests()
    {
        repair = new MediaMtxSourceRepair(logger);
    }

    [Fact]
    public async Task A_control_api_outage_over_250_diverged_paths_warns_250_times_then_not_again_until_it_recovers_and_fails_anew()
    {
        var paths = NewPaths(250);
        paths.ForEach(path =>
        {
            Diverge(path);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.Unauthorized));
        });

        await Passes(1);
        RepairWarnings().ShouldBe(250);

        await Passes(5);
        RepairWarnings().ShouldBe(250, "passes 2..6 of the same outage add no Warning");

        paths.ForEach(gateway.StopFailingPatchesFor);
        await Passes(1);
        paths.ShouldAllBe(path => gateway.Configured[path] == Persisted);

        paths.ForEach(path =>
        {
            gateway.Configure(path, Drifted);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.Unauthorized));
        });
        await Passes(2);

        RepairWarnings().ShouldBe(500, "a recovery resets every path, so the second outage is a first failure");
    }

    [Fact]
    public async Task Flapping_between_a_refusal_and_a_timeout_warns_once_per_change_and_not_for_a_repeat()
    {
        var path = NewPaths(1)[0];
        Diverge(path);

        for (var pass = 0; pass < 6; pass++)
        {
            Exception failure = pass % 2 == 0
                ? Refusal(HttpStatusCode.BadRequest)
                : new TimeoutRejectedException("The operation did not complete within the allowed timeout.");
            gateway.FailPatchesFor(path, failure);
            await Passes(1);
        }

        RepairWarnings().ShouldBe(6, "every pass was a change of outcome (spec 388 §6, accepted)");

        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));
        await Passes(3);

        RepairWarnings().ShouldBe(7, "the first 400 after a timeout is a change; the two after it are repeats");
    }

    [Fact]
    public async Task A_404_on_the_re_patch_is_path_gone_at_debug_and_never_a_warning()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        snapshot.CommittedReadOverride = (_, read) =>
            read == 1 ? Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://10.0.8.3/newer")) : Option<StreamSourceUrl>.None;
        var patches = 0;
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            if (patched == path && ++patches == 2)
            {
                fake.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
            }
        };

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(0, "a 404 does not count as repaired (FR-003)");
        gateway.Patches.Count.ShouldBe(2);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        logger.Entries.Count(entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains(path.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("path gone", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task A_404_clears_a_warned_path_so_its_next_refusal_warns_again()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, patched, _) => snapshot.Retire(patched);
        await Passes(1);

        gateway.BeforePatchApplied = null;
        snapshot.Commit(path, Persisted);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        RepairWarnings().ShouldBe(2);
    }

    [Fact]
    public async Task A_404_on_the_re_patch_for_a_live_stream_is_a_warning_not_path_gone()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        snapshot.CommittedReadOverride = (_, _) => Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://10.0.8.3/newer"));
        var notFound = Refusal(HttpStatusCode.NotFound);
        var patches = 0;
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            if (patched == path && ++patches == 2)
            {
                fake.FailPatchesFor(path, notFound);
            }
        };

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(0, "a 404 does not count as repaired");
        gateway.Patches.Count.ShouldBe(2);
        var warnings = logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1, "a 404 on the re-patch for a live stream is a Warning");
        warnings[0].Level.ShouldBe(LogLevel.Warning);
        warnings[0].Message.ShouldBe($"{FailureText} {path}; the next sweep retries.");
        warnings[0].Exception.ShouldBeSameAs(notFound);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
        snapshot.CommittedReads.Count.ShouldBe(2, "the re-read after the first patch and the confirmation of the 404");
    }

    [Fact]
    public async Task A_live_stream_404_warns_and_its_stream_retired_next_pass_logs_nothing_more()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        var notFound = Refusal(HttpStatusCode.NotFound);
        gateway.FailPatchesFor(path, notFound);

        await Passes(1);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1, "a 404 for a live stream warns on its first failure");
        warnings[0].Exception.ShouldBeSameAs(notFound);
        var entriesAfterTheWarning = logger.Entries.Count;

        snapshot.Retire(path);
        await Passes(1);

        logger.Entries.Count.ShouldBe(entriesAfterTheWarning, "a retired stream is not listed, so the pass says nothing about it");
        gateway.Patches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_confirmation_read_that_fails_is_logged_as_the_paths_failure_and_the_pass_continues()
    {
        var paths = NewPaths(2);
        var (failing, healthy) = (paths[0], paths[1]);
        Diverge(failing);
        Diverge(healthy);
        gateway.FailPatchesFor(failing, Refusal(HttpStatusCode.NotFound));
        var readFailure = new InvalidOperationException("database unreachable");
        snapshot.CommittedReadOverride = (readPath, _) => readPath == failing
            ? throw readFailure
            : Option<StreamSourceUrl>.Some(StreamSourceUrl.From(Persisted));

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1, "the other path is still repaired");
        gateway.Configured[healthy].ShouldBe(Persisted);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1, "a failed confirmation read is the path's failure and Warns");
        warnings[0].Message.ShouldBe($"{FailureText} {failing}; the next sweep retries.");
        warnings[0].Exception.ShouldBeSameAs(readFailure);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancellation_during_the_404_confirmation_read_propagates()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        using CancellationTokenSource shutdown = new();
        snapshot.CommittedReadOverride = (_, _) =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => repair.RepairAsync(gateway, snapshot, shutdown.Token));

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ten_thousand_failing_paths_retired_for_one_pass_all_warn_again_when_they_return()
    {
        var paths = NewPaths(10_000);
        paths.ForEach(path =>
        {
            Diverge(path);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));
        });
        await Passes(1);
        RepairWarnings().ShouldBe(10_000);

        paths.ForEach(snapshot.Retire);
        await Passes(1);
        RepairWarnings().ShouldBe(10_000, "a pass with nothing in the database logs nothing");

        paths.ForEach(path => snapshot.Commit(path, Persisted));
        await Passes(1);

        RepairWarnings().ShouldBe(20_000, "every returning path was pruned, so none is suppressed (L4)");
    }

    [Fact]
    public async Task A_success_before_a_cancelled_pass_still_resets_the_path()
    {
        var paths = NewPaths(2);
        var (first, second) = (paths[0], paths[1]);
        Diverge(first);
        gateway.FailPatchesFor(first, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);
        RepairWarnings().ShouldBe(1);

        using CancellationTokenSource shutdown = new();
        gateway.StopFailingPatchesFor(first);
        Diverge(second);
        gateway.FailPatchesFor(second, new OperationCanceledException(shutdown.Token));
        gateway.BeforePatchApplied = (_, patched, _) =>
        {
            if (patched == second)
            {
                shutdown.Cancel();
            }
        };
        await Should.ThrowAsync<OperationCanceledException>(
            () => repair.RepairAsync(gateway, snapshot, shutdown.Token));
        gateway.Configured[first].ShouldBe(Persisted, "the first path was repaired before the pass was cancelled");

        gateway.BeforePatchApplied = null;
        snapshot.Retire(second);
        gateway.Configure(first, Drifted);
        gateway.FailPatchesFor(first, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        RepairWarnings().ShouldBe(2, "the success cleared the path although the pass never completed");
    }

    [Fact]
    public async Task A_path_that_fails_in_a_pass_whose_list_read_throws_keeps_its_state()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));
        await Passes(1);

        gateway.AfterListCaptured = _ => throw new HttpRequestException("list-boom");
        await Should.ThrowAsync<HttpRequestException>(
            () => repair.RepairAsync(gateway, snapshot, CancellationToken.None));
        gateway.AfterListCaptured = null;
        await Passes(1);

        RepairWarnings().ShouldBe(1, "a failed list read leaves the state as it was (FR-005)");
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_callers_cancellation_is_a_logged_failure_not_a_propagated_one()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        var timeout = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
            new TimeoutException());
        gateway.FailPatchesFor(path, timeout);

        await Passes(2);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Exception.ShouldBeSameAs(timeout);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains("TaskCanceledException -", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_entry_carries_the_persisted_or_observed_source_credentials_across_warning_and_debug()
    {
        var path = NewPaths(1)[0];
        snapshot.Commit(path, "rtsp://10.0.8.1/h264?token=SECRET&pw=hunter2");
        gateway.Configure(path, "rtsp://admin:OBSERVED@10.0.8.2/h264");
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.BadRequest));

        await Passes(3);

        logger.Entries.Count.ShouldBe(3);
        logger.Entries.ShouldAllBe(entry =>
            !entry.Message.Contains("SECRET", StringComparison.Ordinal)
            && !entry.Message.Contains("hunter2", StringComparison.Ordinal)
            && !entry.Message.Contains("OBSERVED", StringComparison.Ordinal)
            && !entry.Message.Contains("admin", StringComparison.Ordinal));
    }

    private static List<MediaMtxPath> NewPaths(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => MediaMtxPath.From($"cam-{Guid.NewGuid()}"))];

    private static HttpRequestException Refusal(HttpStatusCode status) =>
        new($"Response status code does not indicate success: {(int)status} ({status}).", null, status);

    private void Diverge(MediaMtxPath path)
    {
        snapshot.Commit(path, Persisted);
        gateway.Configure(path, Drifted);
    }

    private int RepairWarnings() =>
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning
            && entry.Message.StartsWith(FailureText, StringComparison.Ordinal));

    private async Task Passes(int count)
    {
        for (var pass = 0; pass < count; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }
    }
}
