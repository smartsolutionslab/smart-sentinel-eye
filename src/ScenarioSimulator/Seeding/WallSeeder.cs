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
    /// <summary>
    /// Tries to create the wall of every active scenario. Each is independent: a
    /// plant whose cameras have not all arrived yet simply waits for the next
    /// event, and does not hold up the ones that are ready.
    /// </summary>
    public async Task TryCreateAsync(CancellationToken cancellationToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;

        foreach (string key in scenarios.Active)
        {
            if (!scenarios.Scenarios.TryGetValue(key, out ScenarioDefinition? scenario) || scenario.Wall is null)
            {
                continue;
            }

            await TryCreateOneAsync(key, scenario, cancellationToken);
        }
    }

    private async Task TryCreateOneAsync(
        string key,
        ScenarioDefinition scenario,
        CancellationToken cancellationToken)
    {
        int expected = scenario.Assets.Count(asset => asset.Overlay is not null && asset.Tile is not null);
        if (!correlation.IsWallComplete(key, expected, out int ready))
        {
            logger.WallNotYetComplete(ready, expected);
            return;
        }

        if (!correlation.TryClaimWallCreation(key))
        {
            return;
        }

        try
        {
            await layouts.EnsureWallAsync(
                scenario.Wall!.Name,
                scenario.Wall.Rows,
                scenario.Wall.Cols,
                correlation.CompleteTiles(key),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Release so a later CameraRegisteredV1 retries the wall creation.
            correlation.ReleaseWallClaim(key);
            logger.AssetMissingField(scenario.Wall!.Name, $"wall ({ex.Message})");
        }
    }
}
