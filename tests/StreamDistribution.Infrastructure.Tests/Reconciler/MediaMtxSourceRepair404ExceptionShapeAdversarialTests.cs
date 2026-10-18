using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #3013 / spec 402, adversarial pass: which exception shapes reach the
/// confirmation read, whose cancellation propagates, what the log lines carry,
/// and two repair instances (the startup reconciler's and the sweep's) seeing
/// one fault.
/// </summary>
public class MediaMtxSourceRepair404ExceptionShapeAdversarialTests
{
    private const string FailureText = "Reconciler failed to repair MediaMTX path";
    private const string Persisted = "rtsp://10.0.8.1/h264";
    private const string Drifted = "rtsp://10.0.8.2/h264";

    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
    private static readonly MediaMtxPath PathB = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepair404ExceptionShapeAdversarialTests()
    {
        repair = new MediaMtxSourceRepair(logger);
    }

    [Theory]
    [InlineData("status-less HttpRequestException naming 404")]
    [InlineData("404 wrapped in AggregateException")]
    [InlineData("404 as the inner exception")]
    [InlineData("410 Gone")]
    [InlineData("405 Method Not Allowed")]
    public async Task A_failure_that_is_not_an_HttpRequestException_with_status_404_makes_no_read_and_warns(string shape)
    {
        Diverge(PathA);
        var failure = Shape(shape);
        gateway.FailPatchesFor(PathA, failure);

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        snapshot.CommittedReads.ShouldBeEmpty();
        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        warning.Exception.ShouldBeSameAs(failure);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("path gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_HttpClient_timeout_on_the_patch_is_a_failure_with_no_read_and_the_pass_continues()
    {
        Diverge(PathA);
        Diverge(PathB);
        using CancellationTokenSource httpClientTimeout = new();
        await httpClientTimeout.CancelAsync();
        var timeout = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
            new TimeoutException(),
            httpClientTimeout.Token);
        gateway.FailPatchesFor(PathA, timeout);

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1);
        snapshot.CommittedReads.ShouldBe([PathB], "only the healthy path's post-patch re-read");
        logger.Entries.Single(entry => entry.Level == LogLevel.Warning).Exception.ShouldBeSameAs(timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_cancellation_shaped_read_failure_not_caused_by_the_caller_is_the_paths_failure_and_the_pass_continues(bool taskCanceled)
    {
        Diverge(PathA);
        Diverge(PathB);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.NotFound));
        using CancellationTokenSource commandTimeout = new();
        await commandTimeout.CancelAsync();
        var readFailure = taskCanceled
            ? new TaskCanceledException("command timeout", null, commandTimeout.Token)
            : new OperationCanceledException(commandTimeout.Token);
        snapshot.CommittedReadOverride = (path, _) => path == PathA
            ? throw readFailure
            : Option<StreamSourceUrl>.Some(StreamSourceUrl.From(Persisted));

        var repaired = await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1, "the caller did not cancel, so the pass goes on");
        gateway.Configured[PathB].ShouldBe(Persisted);
        logger.Entries.Single(entry => entry.Level == LogLevel.Warning).Exception.ShouldBeSameAs(readFailure);
    }

    [Fact]
    public async Task A_404_that_arrives_after_the_caller_cancelled_makes_no_read_and_logs_nothing()
    {
        Diverge(PathA);
        using CancellationTokenSource shutdown = new();
        var notFound = Refusal(HttpStatusCode.NotFound);
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            shutdown.Cancel();
            fake.FailPatchesFor(patched, notFound);
        };

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => repair.RepairAsync(gateway, snapshot, shutdown.Token));

        thrown.ShouldBeSameAs(notFound, "the catch filter is the caller's token, so the 404 itself escapes (pre-existing, spec 388 FR-007)");
        snapshot.CommittedReads.ShouldBeEmpty();
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Debug && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_live_404_warning_names_the_path_only_and_no_line_carries_a_credential_or_token()
    {
        snapshot.Commit(PathA, "rtsp://cam-a.fab/s?token=SECRET-PERSISTED");
        gateway.Configure(PathA, "rtsp://operator:SECRET-OBSERVED@cam-a.fab/s");
        snapshot.Commit(PathB, "rtsp://cam-b.fab/s?token=SECRET-FIRST");
        gateway.Configure(PathB, "rtsp://operator:SECRET-OBSERVED@cam-b.fab/s");
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.NotFound));
        snapshot.CommittedReadOverride = (path, _) => path == PathB
            ? Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://cam-b.fab/s?token=SECRET-NEWER"))
            : Option<StreamSourceUrl>.Some(StreamSourceUrl.From("rtsp://cam-a.fab/s?token=SECRET-PERSISTED"));
        var patchesOfB = 0;
        gateway.BeforePatchApplied = (fake, patched, _) =>
        {
            if (patched == PathB && ++patchesOfB == 2)
            {
                fake.FailPatchesFor(PathB, Refusal(HttpStatusCode.NotFound));
            }
        };

        for (var pass = 0; pass < 3; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }

        logger.Entries.Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)
            .ShouldBe(
                [$"{FailureText} {PathA}; the next sweep retries.", $"{FailureText} {PathB}; the next sweep retries."],
                ignoreOrder: true);
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal)
            && !entry.Message.Contains("operator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_path_gone_line_keeps_its_template_and_level_word_for_word()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.NotFound));
        gateway.BeforePatchApplied = (_, patched, _) => snapshot.Retire(patched);

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        var entry = logger.Entries.Single();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe($"Reconciler skipped MediaMTX path {PathA}: path gone (404), its stream was retired during the pass.");
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task The_startup_reconcilers_repair_and_the_sweeps_each_warn_once_for_one_persistent_live_404()
    {
        Diverge(PathA);
        gateway.FailPatchesFor(PathA, Refusal(HttpStatusCode.NotFound));
        MediaMtxSourceRepair startup = new(logger);

        await Task.WhenAll(
            startup.RepairAsync(gateway, snapshot, CancellationToken.None),
            repair.RepairAsync(gateway, snapshot, CancellationToken.None));
        for (var pass = 0; pass < 3; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }

        logger.Entries.Count(entry => entry.Level == LogLevel.Warning)
            .ShouldBe(2, "each instance owns its own failure log, so a boot sees the fault twice and the sweep never again");
    }

    private static Exception Shape(string shape) => shape switch
    {
        "status-less HttpRequestException naming 404" => new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."),
        "404 wrapped in AggregateException" => new AggregateException(Refusal(HttpStatusCode.NotFound)),
        "404 as the inner exception" => new HttpRequestException("patch failed", Refusal(HttpStatusCode.NotFound)),
        "410 Gone" => Refusal(HttpStatusCode.Gone),
        "405 Method Not Allowed" => Refusal(HttpStatusCode.MethodNotAllowed),
        _ => throw new InvalidOperationException($"unknown shape {shape}"),
    };

    private static HttpRequestException Refusal(HttpStatusCode status) =>
        new($"Response status code does not indicate success: {(int)status} ({status}).", null, status);

    private void Diverge(MediaMtxPath path)
    {
        snapshot.Commit(path, Persisted);
        gateway.Configure(path, Drifted);
    }
}
