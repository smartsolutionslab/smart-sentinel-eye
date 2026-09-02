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
/// asset key (the path) and asks <see cref="WallSeeder"/> to build the wall once
/// the four-way join is satisfied (ADR-0112). The queue is namespaced per
/// ADR-0088 so it does not compete with the StreamDistribution consumer of the
/// same event.
///
/// <para>
/// This is the live-registration path only. It does not fire for cameras that
/// already exist, so <c>ScenarioSeeder</c> drives the same
/// <see cref="WallSeeder"/> at the end of its pass — otherwise a restart with a
/// seeded catalog could never rebuild a missing wall.
/// </para>
/// </summary>
public sealed class CameraRegisteredSimHandler(
    CameraSimProvisioner provisioner,
    AssetCorrelationTable correlation,
    WallSeeder wall,
    IOptions<ScenarioOptions> scenarioOptions,
    ILogger<CameraRegisteredSimHandler> logger)
{
    public async Task Handle(CameraRegisteredV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (camera, name, url, _, _, _) = message;

        if (!RtspPath.TryExtract(url, out string path))
        {
            // Cameras not registered by this simulator (no path component) are
            // none of our business — log and drop, don't retry.
            logger.SkippedNonSimulatedCamera(url);
            return;
        }

        // Record the camera and (on the four-way join) build the wall FIRST — the
        // wall needs only the camera id, not a provisioned loop path. Provisioning
        // is best-effort and runs last: if it throws Wolverine retries the whole
        // handler, and RecordCamera + the wall claim/read-back are both idempotent,
        // so a camera-sim hiccup can never block the wall from being created.
        correlation.RecordCamera(path, camera);
        logger.AssetCameraRecorded(path, camera);

        await wall.TryCreateAsync(cancellationToken);

        // A scenario asset plays its own clip; anything else — hand-registered, or
        // whatever a test run left behind — gets the shared clip with its name
        // drawn on it, so it is still answerable by looking (FR-004).
        if (TryFindAssetClip(path, out string clip))
        {
            await provisioner.ProvisionLoopPathAsync(path, clip, cancellationToken);
            return;
        }

        await provisioner.ProvisionLabelledPathAsync(path, name, camera, cancellationToken);
    }

    private bool TryFindAssetClip(string path, out string clip)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;

        foreach (string key in scenarios.Active)
        {
            if (!scenarios.Scenarios.TryGetValue(key, out ScenarioDefinition? scenario))
            {
                continue;
            }

            AssetDefinition? asset = scenario.Assets
                .FirstOrDefault(candidate => string.Equals(candidate.Camera.Path, path, StringComparison.Ordinal));

            if (asset is not null)
            {
                clip = asset.Camera.Clip;
                return true;
            }
        }

        clip = string.Empty;
        return false;
    }
}
