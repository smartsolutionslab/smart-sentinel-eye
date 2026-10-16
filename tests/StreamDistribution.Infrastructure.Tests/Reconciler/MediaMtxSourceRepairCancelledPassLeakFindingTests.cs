using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

public class MediaMtxSourceRepairCancelledPassLeakFindingTests
{
    [Fact]
    public async Task A_path_that_converged_after_a_cancelled_pass_warns_on_its_next_failure()
    {
        SourceRepairGateway gateway = new();
        SourceRepairSnapshot snapshot = new();
        CapturingLogger<MediaMtxReconciler> logger = new();
        MediaMtxSourceRepair repair = new(logger);
        var first = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var second = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        snapshot.Commit(first, "rtsp://10.0.8.1/h264");
        gateway.Configure(first, "rtsp://10.0.8.2/h264");
        gateway.FailPatchesFor(first, Refusal());
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        using CancellationTokenSource shutdown = new();
        snapshot.Commit(second, "rtsp://10.0.8.1/h264");
        gateway.Configure(second, "rtsp://10.0.8.2/h264");
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

        gateway.BeforePatchApplied = null;
        snapshot.Retire(second);
        gateway.Configure(first, "rtsp://10.0.8.1/h264");
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Configure(first, "rtsp://10.0.8.2/h264");
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(
            2,
            "the path converged in a completed pass, so its next failure is a first failure (spec 388 A1)");
    }

    private static HttpRequestException Refusal() =>
        new("Response status code does not indicate success: 400 (Bad Request).", null, HttpStatusCode.BadRequest);
}
