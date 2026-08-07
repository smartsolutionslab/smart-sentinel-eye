using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Background service that, once on startup, reads the active scenario and seeds
/// it (ADR-0111). For each asset it seeds the per-station overlay (Phase A,
/// capturing its id + tile for the wall join) + the highlight rule (Phase B),
/// then registers the camera (Phase C) — which publishes <c>CameraRegisteredV1</c>,
/// consumed by <c>CameraRegisteredSimHandler</c> to provision the loop path and,
/// once all four stations are complete, create the single 2×2 wall (Phase D).
/// Idempotent throughout (stable names; 409/existing → reuse), so a restart
/// re-syncs without duplicating.
/// </summary>
public sealed class ScenarioSeeder(
    CameraCatalogClient catalog,
    OverlayDesignerClient overlays,
    AutomationRulesClient rules,
    AssetCorrelationTable correlation,
    IOptions<ScenarioOptions> scenarioOptions,
    WallSeeder wall,
    ILogger<ScenarioSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;

        if (!scenarios.Scenarios.TryGetValue(scenarios.Active, out ScenarioDefinition scenario))
        {
            logger.ScenarioNotFound(scenarios.Active);
            return;
        }

        logger.SeedingScenario(scenario.Name, scenario.Assets.Count);

        foreach (AssetDefinition asset in scenario.Assets)
        {
            await SeedOverlayAndRuleAsync(asset, stoppingToken);

            // Record the id whether the camera was created or already existed:
            // it is what correlates the camera to its wall tile, and
            // CameraRegisteredV1 only supplies it for a genuinely new one. Null
            // means the read-back could not determine it (logged there); the
            // wall simply stays incomplete rather than the seed failing.
            Guid? camera = await catalog.RegisterCameraAsync(asset.Name, asset.Camera.Path, stoppingToken);
            if (camera.HasValue)
            {
                correlation.RecordCamera(asset.Camera.Path, camera.Value);
            }
        }

        // The event handler covers live registrations; this covers the restart
        // where every camera already exists, so no event fires and the wall
        // would otherwise never be rebuilt. Both are idempotent.
        await wall.TryCreateAsync(stoppingToken);

        logger.ScenarioSeeded(scenario.Name);
    }

    private async Task SeedOverlayAndRuleAsync(AssetDefinition asset, CancellationToken cancellationToken)
    {
        if (asset.Overlay is null || asset.Tile is null)
        {
            return;
        }

        string assetKey = asset.Camera.Path;
        OverlayLabel label = new(
            asset.Overlay.Label,
            (decimal)asset.Overlay.X,
            (decimal)asset.Overlay.Y,
            (decimal)asset.Overlay.Width,
            (decimal)asset.Overlay.Height,
            (int)asset.Overlay.FontSize);

        Guid overlay = await overlays.EnsureOverlayAsync($"rolling-mill-{asset.Key}", label, cancellationToken);
        correlation.RecordOverlay(assetKey, overlay, asset.Tile.Row, asset.Tile.Col);

        if (asset.Highlight is null)
        {
            logger.AssetMissingField(asset.Key, "highlight");
            return;
        }

        string triggerSource = asset.Sensors
                .FirstOrDefault(sensor => string.Equals(sensor.Kind, asset.Highlight.TriggerKind, StringComparison.Ordinal))?.Source
            ?? "plc";

        await rules.EnsureRuleAsync(
            $"rolling-mill-{asset.Key}-highlight",
            triggerSource,
            asset.Highlight.TriggerKind,
            assetKey,
            asset.Highlight.Comparison,
            asset.Highlight.Threshold,
            overlay,
            asset.Highlight.DurationMs,
            cancellationToken);
    }
}
