using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #3013 / spec 402, adversarial pass: a 404 is "path gone" only when the
/// confirmation read finds no live row. Volume over many passes, mixed outcomes
/// in one pass, and the read racing a retire or a re-register. One
/// <see cref="MediaMtxSourceRepair"/> instance serves every pass, as the sweep's does.
/// </summary>
public class MediaMtxSourceRepair404ConfirmationAdversarialTests
{
    private const string FailureText = "Reconciler failed to repair MediaMTX path";
    private const string Persisted = "rtsp://10.0.8.1/h264";
    private const string Drifted = "rtsp://10.0.8.2/h264";

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly ManualTimeProvider clock = new(DateTimeOffset.Parse("2026-10-18T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepair404ConfirmationAdversarialTests()
    {
        repair = new MediaMtxSourceRepair(logger, clock);
    }

    [Fact]
    public async Task A_persistent_404_on_250_live_paths_for_thirty_minutes_warns_four_times_per_path_and_reads_once_per_path_per_pass()
    {
        var paths = NewPaths(250);
        paths.ForEach(path =>
        {
            Diverge(path);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        });

        for (var pass = 0; pass <= 180; pass++)
        {
            var readsBefore = snapshot.CommittedReads.Count;
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

            (snapshot.CommittedReads.Count - readsBefore).ShouldBe(250, $"pass {pass}: one confirmation read per 404ing path (A2), never more");
            TrackedFailures().ShouldBe(250, $"pass {pass}: one state per failing path, no growth");
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        paths.ShouldAllBe(path => WarningsFor(path) == 4, "Warnings at 0, 10, 20 and 30 minutes and none in between");
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));

        paths.ForEach(snapshot.Retire);
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        TrackedFailures().ShouldBe(0, "a retired path is not listed and its state is pruned");
    }

    [Fact]
    public async Task A_fresh_404ing_path_every_pass_with_the_previous_one_retired_never_accumulates_state()
    {
        var previous = Option<MediaMtxPath>.None;
        for (var pass = 0; pass < 1_000; pass++)
        {
            if (previous.HasValue)
            {
                snapshot.Retire(previous.Value);
            }

            var path = NewPaths(1)[0];
            Diverge(path);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

            TrackedFailures().ShouldBe(1, $"pass {pass}: only this pass's failing path is held");
            previous = Option<MediaMtxPath>.Some(path);
        }

        RepairWarnings().ShouldBe(1_000);
    }

    [Fact]
    public async Task A_path_the_database_reports_gone_every_pass_is_never_held_and_never_warns()
    {
        var paths = NewPaths(250);
        paths.ForEach(path =>
        {
            Diverge(path);
            gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        });
        snapshot.CommittedReadOverride = (_, _) => Option<StreamSourceUrl>.None;

        for (var pass = 0; pass < 20; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
            TrackedFailures().ShouldBe(0);
        }

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        snapshot.CommittedReads.Count.ShouldBe(250 * 20);
    }

    [Theory]
    [InlineData("01234")]
    [InlineData("43210")]
    [InlineData("24130")]
    [InlineData("30421")]
    public async Task Five_outcomes_in_one_pass_are_each_classified_on_their_own_whatever_the_order(string order)
    {
        var paths = NewPaths(5);
        var (live404, retired404, refused, healthy, readThrows) = (paths[0], paths[1], paths[2], paths[3], paths[4]);
        foreach (var role in order)
        {
            Diverge(paths[role - '0']);
        }

        var liveNotFound = Refusal(HttpStatusCode.NotFound);
        var badRequest = Refusal(HttpStatusCode.BadRequest);
        var readFailure = new InvalidOperationException("database unreachable");
        gateway.FailPatchesFor(live404, liveNotFound);
        gateway.FailPatchesFor(retired404, Refusal(HttpStatusCode.NotFound));
        gateway.FailPatchesFor(refused, badRequest);
        gateway.FailPatchesFor(readThrows, Refusal(HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, patched, _) =>
        {
            if (patched == retired404)
            {
                snapshot.Retire(patched);
            }
        };
        var readStillFails = true;
        snapshot.CommittedReadOverride = (path, _) =>
        {
            if (path == readThrows && readStillFails)
            {
                throw readFailure;
            }

            return path == retired404
                ? Option<StreamSourceUrl>.None
                : Option<StreamSourceUrl>.Some(StreamSourceUrl.From(Persisted));
        };

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1);
        gateway.Configured[healthy].ShouldBe(Persisted);
        Warning(live404).ShouldBeSameAs(liveNotFound);
        Warning(refused).ShouldBeSameAs(badRequest);
        Warning(readThrows).ShouldBeSameAs(readFailure);
        WarningsFor(retired404).ShouldBe(0);
        WarningsFor(healthy).ShouldBe(0);
        PathGoneLines().ShouldBe([retired404]);
        snapshot.CommittedReads.Count(path => path == refused).ShouldBe(0, "a 400 makes no confirmation read");
        snapshot.CommittedReads.Count(path => path == live404).ShouldBe(1);
        snapshot.CommittedReads.Count(path => path == retired404).ShouldBe(1);
        snapshot.CommittedReads.Count(path => path == readThrows).ShouldBe(1);
        snapshot.CommittedReads.Count(path => path == healthy).ShouldBe(1, "the post-patch re-read only");
        TrackedFailures().ShouldBe(3);

        readStillFails = false;
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        RepairWarnings().ShouldBe(4, "only the path whose read recovered changed outcome (database fault -> 404)");
        logger.Entries.Last(entry => entry.Level == LogLevel.Warning).Exception.ShouldBeOfType<HttpRequestException>();
        PathGoneLines().ShouldBe([retired404], "the retired path is not listed again");
        TrackedFailures().ShouldBe(3);
    }

    [Fact]
    public async Task A_stream_retired_right_after_the_read_said_live_warns_once_then_is_pruned_and_warns_afresh_if_it_returns()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        snapshot.AfterCommittedReadCaptured = (fake, read) => fake.Retire(read);

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        RepairWarnings().ShouldBe(1, "the read answered Some, so the 404 is a live-stream failure");

        snapshot.AfterCommittedReadCaptured = null;
        var entriesBefore = logger.Entries.Count;
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        logger.Entries.Count.ShouldBe(entriesBefore, "a retired stream is not listed, so nothing is logged");
        TrackedFailures().ShouldBe(0);

        snapshot.Commit(path, Persisted);
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        RepairWarnings().ShouldBe(2, "pruned state means the returning path's 404 is a first failure");
    }

    [Fact]
    public async Task A_stream_re_registered_right_after_the_read_said_gone_warns_on_its_next_404()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, patched, _) => snapshot.Retire(patched);
        snapshot.AfterCommittedReadCaptured = (fake, read) => fake.Commit(read, Persisted);

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        PathGoneLines().ShouldBe([path]);
        RepairWarnings().ShouldBe(0);

        gateway.BeforePatchApplied = null;
        snapshot.AfterCommittedReadCaptured = null;
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        RepairWarnings().ShouldBe(1, "the stream is live again and MediaMTX still answers 404");
        PathGoneLines().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_retire_race_inside_the_repeat_window_clears_the_state_so_the_live_404_after_it_warns_at_once()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        gateway.FailPatchesFor(path, Refusal(HttpStatusCode.NotFound));
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.BeforePatchApplied = (_, patched, _) => snapshot.Retire(patched);
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        gateway.BeforePatchApplied = null;
        snapshot.Commit(path, Persisted);
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        RepairWarnings().ShouldBe(2, "Gone clears the state (spec 388 FR-003 behaviour kept), so the return is a first failure");
        PathGoneLines().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_404_on_the_first_patch_and_then_on_the_re_patch_is_one_outcome_and_warns_once()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        const string Newer = "rtsp://10.0.8.3/newer";
        var notFound = Refusal(HttpStatusCode.NotFound);
        gateway.AfterPatchApplied = (fake, patched, _) =>
        {
            snapshot.Commit(patched, Newer);
            fake.FailPatchesFor(patched, notFound);
        };

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        gateway.Patches.Count.ShouldBe(2, "the first patch landed, the re-patch answered 404");
        gateway.AfterPatchApplied = null;

        for (var pass = 0; pass < 5; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }

        RepairWarnings().ShouldBe(1, "a re-patch 404 and a first-patch 404 for the same live stream are the same outcome");
        snapshot.CommittedReads.Count.ShouldBe(2 + 5, "the re-read and the confirmation, then one confirmation per pass");
    }

    [Fact]
    public async Task A_404_on_the_re_patch_whose_confirmation_read_fails_warns_with_the_read_failure()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        var readFailure = new TimeoutException("database did not answer");
        snapshot.CommittedReadOverride = (_, read) => read == 1
            ? Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://10.0.8.3/newer"))
            : throw readFailure;
        var patches = 0;
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            if (++patches == 2)
            {
                fake.FailPatchesFor(patched, Refusal(HttpStatusCode.NotFound));
            }
        };

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(0);
        Warning(path).ShouldBeSameAs(readFailure);
        RepairWarnings().ShouldBe(1);
        PathGoneLines().ShouldBeEmpty();
        snapshot.CommittedReads.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_cancellation_during_the_re_patch_confirmation_read_propagates_and_logs_no_warning()
    {
        var path = NewPaths(1)[0];
        Diverge(path);
        using CancellationTokenSource shutdown = new();
        snapshot.CommittedReadOverride = (_, read) =>
        {
            if (read == 1)
            {
                return Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://10.0.8.3/newer"));
            }

            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        var patches = 0;
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            if (++patches == 2)
            {
                fake.FailPatchesFor(patched, Refusal(HttpStatusCode.NotFound));
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => repair.RepairAsync(gateway, snapshot, shutdown.Token));

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        PathGoneLines().ShouldBeEmpty();
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

    private int WarningsFor(MediaMtxPath path) =>
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning
            && entry.Message == $"{FailureText} {path}; the next sweep retries.");

    private Exception? Warning(MediaMtxPath path) =>
        logger.Entries.Single(entry => entry.Level == LogLevel.Warning
            && entry.Message == $"{FailureText} {path}; the next sweep retries.").Exception;

    private List<MediaMtxPath> PathGoneLines() =>
        [.. snapshot.CommittedReads.Distinct().Where(path => logger.Entries.Any(entry => entry.Level == LogLevel.Debug
            && entry.Message == $"Reconciler skipped MediaMTX path {path}: path gone (404), its stream was retired during the pass."))];

    // The per-path state is private and has no observable size; reflection is the
    // only way to prove it stays bounded rather than inferring it from log volume.
    private int TrackedFailures()
    {
        var failureLog = typeof(MediaMtxSourceRepair)
            .GetField("failureLog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(repair)!;
        var failures = (System.Collections.ICollection)failureLog.GetType()
            .GetField("failures", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(failureLog)!;

        return failures.Count;
    }
}
