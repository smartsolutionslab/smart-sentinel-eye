using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// Spec 381 / issue #2993. A MediaMTX-reported source whose userinfo the parser reads as host and port is
/// logged by the repair as the placeholder, never as an origin holding the credential.
/// </summary>
public class MediaMtxSourceRepairUserInfoLogRedactionTests
{
    private const string Credential = "operator:2468";
    private const string CommittedSource = "rtsp://10.0.8.1/h264";

    private static readonly MediaMtxPath Path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();

    [Fact]
    public async Task A_cut_short_source_mediamtx_holds_is_logged_as_the_placeholder()
    {
        snapshot.Commit(Path, CommittedSource);
        gateway.Configure(Path, "rtsp://operator:2468#@cam/x");

        await new MediaMtxSourceRepair(logger).RepairAsync(gateway, snapshot, CancellationToken.None);

        logger.Entries.ShouldContain(
            entry => entry.Message == $"Reconciler repaired MediaMTX path {Path} source <unparseable url> -> rtsp://10.0.8.1.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Credential, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cut_short_source_mediamtx_holds_is_still_repaired_to_the_committed_source()
    {
        snapshot.Commit(Path, CommittedSource);
        gateway.Configure(Path, "rtsp://operator:2468#@cam/x");

        await new MediaMtxSourceRepair(logger).RepairAsync(gateway, snapshot, CancellationToken.None);

        gateway.Configured[Path].ShouldBe(CommittedSource);
    }
}
