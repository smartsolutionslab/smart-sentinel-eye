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

        int reconciled = 0;
        int total = 0;

        // Every active scenario, and a missing key skips only its own — one bad
        // entry in the list must not take the other plants down with it.
        foreach (string key in scenarios.Active)
        {
            if (!scenarios.Scenarios.TryGetValue(key, out ScenarioDefinition scenario))
            {
                logger.ScenarioNotFound(key);
                continue;
            }

            total += scenario.Assets.Count;

            foreach (CameraDefinition sim in scenario.Assets.Select(asset => asset.Camera))
            {
                try
                {
                    await provisioner.ProvisionLoopPathAsync(sim.Path, sim.Clip, cancellationToken);
                    reconciled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best-effort: a camera-sim outage must not block the host from
                    // starting. The remaining assets still reconcile, and the next
                    // restart retries this one.
                    logger.CameraSimReconcileFailed(sim.Path, ex.Message);
                }
            }
        }

        logger.CameraSimReconciled(reconciled, total);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
