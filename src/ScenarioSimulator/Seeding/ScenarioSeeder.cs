using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Background service that, once on startup, reads the active scenario and
/// registers each asset's camera in the catalog (ADR-0111 M1). Registration is
/// the trigger: camera-catalog publishes <c>CameraRegisteredV1</c>, which this
/// same worker consumes (<c>CameraRegisteredSimHandler</c>) to provision the
/// matching loop path on camera-sim. Idempotent — a duplicate name returns 409
/// and is skipped, so a restart re-syncs without duplicating.
/// </summary>
public sealed class ScenarioSeeder(
    CameraCatalogClient catalog,
    IOptions<ScenarioOptions> scenarioOptions,
    IOptions<SimulatorOptions> simulatorOptions,
    ILogger<ScenarioSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;
        SimulatorOptions runtime = simulatorOptions.Value;

        if (!scenarios.Scenarios.TryGetValue(scenarios.Active, out ScenarioDefinition scenario))
        {
            logger.ScenarioNotFound(scenarios.Active);
            return;
        }

        logger.SeedingScenario(scenario.Name, scenario.Assets.Count);

        foreach (AssetDefinition asset in scenario.Assets)
        {
            string rtspUrl = $"rtsp://{runtime.RtspHost.Trim('/')}/{asset.Camera.Path}";
            await catalog.RegisterCameraAsync(asset.Name, rtspUrl, stoppingToken);

            // M2 EXTENSION POINT (NOT IMPLEMENTED — ADR-0111):
            // Per asset.Sensors, start a simulated PLC / inference device that
            // publishes MQTT on event-ingestion's per-device topic, correlated
            // by asset.Key, on a timeline. The asset identity + scenario file are
            // already shared, so M2 plugs in here with no rework.
        }

        logger.ScenarioSeeded(scenario.Name);
    }
}
