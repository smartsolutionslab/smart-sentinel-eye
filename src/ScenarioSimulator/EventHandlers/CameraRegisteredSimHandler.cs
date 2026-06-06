using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.EventHandlers;

/// <summary>
/// Wolverine subscriber that closes the catalog -> sim loop (ADR-0111 M1) and,
/// for M2, completes the rolling-mill wall. On each <c>CameraRegisteredV1</c> it
/// provisions the camera-sim loop path, then records the camera id against its
/// asset key (the path); when all four stations have both camera + overlay it
/// creates the single 2×2 wall exactly once (the four-way join, ADR-0112). The
/// queue is namespaced per ADR-0088 so it does not compete with the
/// StreamDistribution consumer of the same event.
/// </summary>
public sealed class CameraRegisteredSimHandler(
    CameraSimProvisioner provisioner,
    AssetCorrelationTable correlation,
    LayoutCompositionClient layouts,
    IOptions<ScenarioOptions> scenarioOptions,
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

        // Record the camera and (on the four-way join) build the wall FIRST — the
        // wall needs only the camera id, not a provisioned loop path. Provisioning
        // is best-effort and runs last: if it throws Wolverine retries the whole
        // handler, and RecordCamera + the wall claim/read-back are both idempotent,
        // so a camera-sim hiccup can never block the wall from being created.
        correlation.RecordCamera(path, message.Camera);
        logger.AssetCameraRecorded(path, message.Camera);

        await TryCreateWallAsync(cancellationToken);

        await provisioner.ProvisionLoopPathAsync(path, cancellationToken);
    }

    private async Task TryCreateWallAsync(CancellationToken cancellationToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;
        if (!scenarios.Scenarios.TryGetValue(scenarios.Active, out ScenarioDefinition scenario) || scenario.Wall is null)
        {
            return;
        }

        int expected = scenario.Assets.Count(asset => asset.Overlay is not null && asset.Tile is not null);
        if (!correlation.IsWallComplete(expected, out int ready))
        {
            logger.WallNotYetComplete(ready, expected);
            return;
        }

        if (!correlation.TryClaimWallCreation())
        {
            return;
        }

        try
        {
            await layouts.EnsureWallAsync(
                scenario.Wall.Name,
                scenario.Wall.Rows,
                scenario.Wall.Cols,
                correlation.CompleteTiles(),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Release so a later CameraRegisteredV1 retries the wall creation.
            correlation.ReleaseWallClaim();
            logger.AssetMissingField(scenario.Wall.Name, $"wall ({ex.Message})");
        }
    }
}
