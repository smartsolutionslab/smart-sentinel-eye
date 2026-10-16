using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2994 / spec 388 FR-003 and FR-006. A failure that persists Warns again ten
/// minutes after the last Warning (inclusive), measured on the monotonic
/// timestamp: a Debug line does not move the window, and a wall-clock step
/// neither silences nor floods it.
/// </summary>
public class MediaMtxSourceRepairRelogIntervalTests
{
    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly ManualTimeProvider clock = new(DateTimeOffset.Parse("2026-10-16T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepairRelogIntervalTests()
    {
        repair = new MediaMtxSourceRepair(logger, clock);
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.FailPatchesFor(PathA, Refusal());
    }

    [Fact]
    public async Task The_same_failure_warns_again_exactly_ten_minutes_after_the_last_warning()
    {
        await Pass();
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        await Pass();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Pass();
        clock.Advance(TimeSpan.FromMinutes(5));
        await Pass();
        clock.Advance(TimeSpan.FromMinutes(5));
        await Pass();

        var warnings = Warnings();
        warnings.Count.ShouldBe(3);
        warnings.ShouldAllBe(entry => entry.Exception is HttpRequestException);
    }

    [Fact]
    public async Task A_wall_clock_step_back_does_not_silence_a_repeat_that_is_due()
    {
        await Pass();
        clock.StepWallClock(TimeSpan.FromHours(-1));
        clock.Advance(TimeSpan.FromMinutes(10));
        await Pass();

        Warnings().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_wall_clock_step_forward_does_not_cause_a_repeat_that_is_not_due()
    {
        await Pass();
        clock.StepWallClock(TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        await Pass();

        Warnings().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_debug_line_does_not_move_the_window()
    {
        await Pass();
        clock.Advance(TimeSpan.FromMinutes(9));
        await Pass();
        clock.Advance(TimeSpan.FromMinutes(1));
        await Pass();

        Warnings().Count.ShouldBe(2);
    }

    private static HttpRequestException Refusal() =>
        new("Response status code does not indicate success: 400 (Bad Request).", null, HttpStatusCode.BadRequest);

    private List<(LogLevel Level, string Message, Exception? Exception)> Warnings() =>
        [.. logger.Entries.Where(entry => entry.Level == LogLevel.Warning)];

    private Task<int> Pass() => repair.RepairAsync(gateway, snapshot, CancellationToken.None);
}
