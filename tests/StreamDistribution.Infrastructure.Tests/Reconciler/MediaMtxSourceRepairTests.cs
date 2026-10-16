using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378. A retried PATCH can land after a newer repoint and leave a
/// MediaMTX path pulling an address the database does not record. The pass
/// compares the two sources (ordinally) and writes the database's value back.
/// </summary>
public class MediaMtxSourceRepairTests
{
    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
    private static readonly MediaMtxPath PathB = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();

    [Fact]
    public async Task A_diverged_source_is_patched_back_to_the_persisted_source()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");

        var repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1);
        gateway.Configured[PathA].ShouldBe("rtsp://10.0.8.1/h264");
        gateway.Patches.ShouldBe([(PathA, "rtsp://10.0.8.1/h264")]);
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Information
                && entry.Message == $"Reconciler repaired MediaMTX path {PathA} source rtsp://10.0.8.2 -> rtsp://10.0.8.1.");
    }

    [Fact]
    public async Task Equal_sources_are_left_alone_across_two_passes()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.1/h264");

        var first = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        var second = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        (first, second).ShouldBe((0, 0));
        gateway.Patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_difference_in_case_or_trailing_slash_is_a_divergence_repaired_once()
    {
        snapshot.Commit(PathA, "rtsp://cam-a/h264");
        gateway.Configure(PathA, "rtsp://CAM-A/h264/");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.ShouldBe(
            [(PathA, "rtsp://cam-a/h264")],
            "ordinal equality: patched exactly once, and the repeat sweep finds equal sources — no patch storm");
        gateway.Configured[PathA].ShouldBe("rtsp://cam-a/h264");
    }

    [Fact]
    public async Task A_repeat_sweep_after_a_repair_sends_nothing()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);
        gateway.Patches.Clear();
        var repeat = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        repeat.ShouldBe(0);
        gateway.Patches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_persisted_value_is_ever_written_never_the_one_mediamtx_reported()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        snapshot.Commit(PathB, "rtsp://10.0.8.5/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.Configure(PathB, "rtsp://10.0.8.6/h264");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.Count.ShouldBe(2);
        foreach (var (path, source) in gateway.Patches)
        {
            source.ShouldBe(snapshot.Committed(path).Value);
        }
    }

    [Fact]
    public async Task A_refused_repair_is_logged_with_the_path_and_the_other_paths_are_still_repaired()
    {
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        snapshot.Commit(PathB, "rtsp://10.0.8.5/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
        gateway.Configure(PathB, "rtsp://10.0.8.6/h264");
        gateway.FailPatchesFor(PathA, new HttpRequestException("400 Bad Request"));

        var repaired = await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(1);
        gateway.Configured[PathB].ShouldBe("rtsp://10.0.8.5/h264");
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(PathA.Value, StringComparison.Ordinal)
                && entry.Exception is HttpRequestException);
    }

    [Fact]
    public async Task Paths_the_pass_does_not_own_are_not_touched()
    {
        var withoutRow = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var retired = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var absentFromMediaMtx = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        gateway.Configure(withoutRow, "rtsp://10.0.8.2/h264");
        gateway.Configure(retired, "rtsp://10.0.8.3/h264");
        snapshot.Commit(retired, "rtsp://10.0.8.4/h264");
        snapshot.Retire(retired);
        snapshot.Commit(absentFromMediaMtx, "rtsp://10.0.8.1/h264");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Patches.ShouldBeEmpty();
        gateway.Added.ShouldBeEmpty();
        gateway.Removed.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_log_line_carries_the_full_source_only_its_origin()
    {
        snapshot.Commit(PathA, "rtsp://cam/stream?token=SECRET");
        gateway.Configure(PathA, "rtsp://old/SECRET?token=SECRET");

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        logger.Entries.ShouldContain(
            entry => entry.Message == $"Reconciler repaired MediaMTX path {PathA} source rtsp://old -> rtsp://cam.");
        logger.Entries.ShouldAllBe(
            entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal)
                && (entry.Exception == null || !entry.Exception.ToString().Contains("SECRET", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_refused_repair_of_a_secret_bearing_source_does_not_leak_it_either()
    {
        snapshot.Commit(PathA, "rtsp://cam/stream?token=SECRET");
        gateway.Configure(PathA, "rtsp://old/stream");
        gateway.FailPatchesFor(PathA, new HttpRequestException("400 Bad Request"));

        await Pass().RepairAsync(gateway, snapshot, CancellationToken.None);

        logger.Entries.ShouldNotBeEmpty();
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal));
    }

    private MediaMtxSourceRepair Pass() => new(logger);
}
