using System.Collections.Concurrent;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Correlates each asset's overlay + camera identifiers (ADR-0111 M2). Overlays
/// are recorded synchronously during seeding; camera IDs arrive asynchronously on
/// <c>CameraRegisteredV1</c>. When every asset of a scenario has both, that
/// scenario's wall is created — exactly once, via the interlocked claim.
/// Keyed by asset key (== the camera path). Thread-safe.
///
/// <para>
/// <b>Scoped per scenario.</b> This held one claim and one flat row set while
/// there was one plant. With three, a flat <c>CompleteTiles()</c> would compose
/// the paper mill's cameras into the rolling mill's wall, and the first scenario
/// to finish would claim wall creation for all of them. Rows carry their
/// scenario and the claim is per scenario; nothing else about the shape changed.
/// </para>
/// </summary>
public sealed class AssetCorrelationTable
{
    private readonly ConcurrentDictionary<string, AssetRow> rows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> wallClaims = new(StringComparer.Ordinal);

    public void RecordOverlay(string scenario, string assetKey, Guid overlay, int tileRow, int tileCol) =>
        rows.AddOrUpdate(
            assetKey,
            _ => new AssetRow { Scenario = scenario, Overlay = overlay, TileRow = tileRow, TileCol = tileCol },
            (_, existing) =>
            {
                existing.Scenario = scenario;
                existing.Overlay = overlay;
                existing.TileRow = tileRow;
                existing.TileCol = tileCol;
                return existing;
            });

    /// <summary>
    /// Records a camera against its asset. No scenario argument: the camera
    /// arrives on an integration event that knows only the path, and the overlay
    /// pass has already established which scenario that path belongs to.
    /// </summary>
    public void RecordCamera(string assetKey, Guid camera) =>
        rows.AddOrUpdate(
            assetKey,
            _ => new AssetRow { Camera = camera },
            (_, existing) =>
            {
                existing.Camera = camera;
                return existing;
            });

    /// <summary>
    /// True when exactly <paramref name="expectedCount"/> assets *of this
    /// scenario* have both overlay and camera.
    /// </summary>
    public bool IsWallComplete(string scenario, int expectedCount, out int ready)
    {
        ready = rows.Values.Count(row =>
            row.Scenario == scenario && row.Overlay.HasValue && row.Camera.HasValue);
        return expectedCount > 0 && ready == expectedCount;
    }

    /// <summary>Wins for exactly one caller per scenario; the winner creates that wall.</summary>
    public bool TryClaimWallCreation(string scenario) =>
        wallClaims.TryAdd(scenario, 1);

    /// <summary>Release the claim so a later event retries (e.g. after a create failure).</summary>
    public void ReleaseWallClaim(string scenario) =>
        wallClaims.TryRemove(scenario, out _);

    /// <summary>This scenario's complete tiles (camera + overlay both known), in row-major order.</summary>
    public IReadOnlyList<CorrelatedTile> CompleteTiles(string scenario) =>
        rows.Values
            .Where(row => row.Scenario == scenario && row.Overlay.HasValue && row.Camera.HasValue)
            .OrderBy(row => row.TileRow)
            .ThenBy(row => row.TileCol)
            .Select(row => new CorrelatedTile(row.Camera!.Value, row.Overlay!.Value, row.TileRow, row.TileCol))
            .ToList();

    private sealed class AssetRow
    {
        public string Scenario { get; set; } = string.Empty;

        public Guid? Overlay { get; set; }

        public Guid? Camera { get; set; }

        public int TileRow { get; set; }

        public int TileCol { get; set; }
    }
}
