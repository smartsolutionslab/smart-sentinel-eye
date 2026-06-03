using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.EventHandlers;

/// <summary>
/// Wolverine subscriber that closes the catalog -> sim loop (ADR-0111 M1): on
/// each <c>CameraRegisteredV1</c> it derives the camera-sim path from the
/// camera's RTSP URL (<c>rtsp://camera-sim:8554/&lt;path&gt;</c>) and provisions
/// a looping-video path on camera-sim. The queue is namespaced
/// <c>scenario-simulator.SmartSentinelEye.Shared.Contracts.CameraCatalog.CameraRegisteredV1</c>
/// per ADR-0088's per-module queue isolation, so it does not compete with the
/// StreamDistribution consumer of the same event.
/// </summary>
public sealed class CameraRegisteredSimHandler(
    CameraSimProvisioner provisioner,
    ILogger<CameraRegisteredSimHandler> logger)
{
    public async Task Handle(CameraRegisteredV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        if (!RtspPath.TryExtract(message.Url, out string path))
        {
            // Cameras not registered by this simulator (no path component) are
            // none of our business — log and drop, don't retry.
            logger.SkippedNonSimulatedCamera(message.Url);
            return;
        }

        await provisioner.ProvisionLoopPathAsync(path, cancellationToken);
    }
}
