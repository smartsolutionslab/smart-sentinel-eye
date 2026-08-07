using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Creates the single 2×2 rolling-mill wall once every asset has both a camera
/// and an overlay (the four-way join, ADR-0112). Extracted so both entry points
/// can reach it: <c>CameraRegisteredSimHandler</c> for a live registration, and
/// <c>ScenarioSeeder</c> at the end of its pass.
///
/// <para>
/// The seeder entry point is what makes this survive a restart. Wall creation
/// used to hang solely off <c>CameraRegisteredV1</c>, which does not fire for
/// cameras that already exist — so a stack with a registered catalog and a
/// missing wall would never build one, however many times it restarted. Same
/// root cause as the camera-sim loop paths.
/// </para>
///
/// <para>
/// Safe to call from both: <c>TryClaimWallCreation</c> admits exactly one
/// caller, and the create endpoint answers 409 for a wall that already exists.
/// </para>
/// </summary>
public sealed class WallSeeder(
    LayoutCompositionClient layouts,
    AssetCorrelationTable correlation,
    IOptions<ScenarioOptions> scenarioOptions,
    ILogger<WallSeeder> logger)
{
    public async Task TryCreateAsync(CancellationToken cancellationToken)
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
