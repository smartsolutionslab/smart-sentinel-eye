using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// One-shot startup pass that provisions the loop path for every asset in the
/// active scenario (ADR-0111). The scenario file is the durable source of
/// truth; camera-sim's paths are runtime state.
///
/// <para>
/// camera-sim paths are added through its v3 config API, so they die with the
/// container. Provisioning ran only from <c>CameraRegisteredV1</c> via
/// <c>CameraRegisteredSimHandler</c>, and that event never fires for cameras
/// that already exist — so any camera-sim restart left the main MediaMTX
/// pulling paths it reported as "not configured", every stream Degraded and
/// no video, until the catalog happened to be wiped. This is the same failure
/// <c>MediaMtxReconciler</c> exists to prevent on the StreamDistribution side;
/// the camera-sim half never got its equivalent.
/// </para>
/// </summary>
public sealed class CameraSimReconciler(
    CameraSimProvisioner provisioner,
    IOptions<ScenarioOptions> scenarioOptions,
    ILogger<CameraSimReconciler> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;

        if (!scenarios.Scenarios.TryGetValue(scenarios.Active, out ScenarioDefinition scenario))
        {
            logger.ScenarioNotFound(scenarios.Active);
            return;
        }

        foreach (string path in scenario.Assets.Select(asset => asset.Camera.Path))
        {
            try
            {
                await provisioner.ProvisionLoopPathAsync(path, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: a camera-sim outage must not block the host from
                // starting. The remaining assets still reconcile, and the next
                // restart retries this one.
                logger.CameraSimReconcileFailed(path, ex.Message);
            }
        }

        logger.CameraSimReconciled(scenario.Assets.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
