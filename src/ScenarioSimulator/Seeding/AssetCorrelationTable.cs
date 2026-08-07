using System.Collections.Concurrent;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Correlates the four assets' overlay + camera identifiers (ADR-0111 M2).
/// Overlays are recorded synchronously during seeding; camera IDs arrive
/// asynchronously on <c>CameraRegisteredV1</c>. When all four assets have both,
/// the single 2×2 wall is created — exactly once, via the interlocked claim.
/// Keyed by asset key (== the camera path). Thread-safe.
/// </summary>
public sealed class AssetCorrelationTable
{
    private readonly ConcurrentDictionary<string, AssetRow> rows = new(StringComparer.Ordinal);
    private int wallClaimed;

    public void RecordOverlay(string assetKey, Guid overlay, int tileRow, int tileCol) =>
        rows.AddOrUpdate(
            assetKey,
            _ => new AssetRow { Overlay = overlay, TileRow = tileRow, TileCol = tileCol },
            (_, existing) =>
            {
                existing.Overlay = overlay;
                existing.TileRow = tileRow;
                existing.TileCol = tileCol;
                return existing;
            });

    public void RecordCamera(string assetKey, Guid camera) =>
        rows.AddOrUpdate(
            assetKey,
            _ => new AssetRow { Camera = camera },
            (_, existing) =>
            {
                existing.Camera = camera;
                return existing;
            });

    /// <summary>True when exactly <paramref name="expectedCount"/> assets have both overlay and camera.</summary>
    public bool IsWallComplete(int expectedCount, out int ready)
    {
        ready = rows.Values.Count(row => row.Overlay.HasValue && row.Camera.HasValue);
        return expectedCount > 0 && ready == expectedCount;
    }

    /// <summary>Wins for exactly one caller; the winner creates the wall.</summary>
    public bool TryClaimWallCreation() => Interlocked.CompareExchange(ref wallClaimed, 1, 0) == 0;

    /// <summary>Release the claim so a later event retries (e.g. after a create failure).</summary>
    public void ReleaseWallClaim() => Interlocked.Exchange(ref wallClaimed, 0);

    /// <summary>The complete tiles (camera + overlay both known), in row-major order.</summary>
    public IReadOnlyList<CorrelatedTile> CompleteTiles() =>
        rows.Values
            .Where(row => row.Overlay.HasValue && row.Camera.HasValue)
            .OrderBy(row => row.TileRow)
            .ThenBy(row => row.TileCol)
            .Select(row => new CorrelatedTile(row.Camera.Value, row.Overlay.Value, row.TileRow, row.TileCol))
            .ToList();

    private sealed class AssetRow
    {
        public Guid? Overlay { get; set; }

        public Guid? Camera { get; set; }

        public int TileRow { get; set; }

        public int TileCol { get; set; }
    }
}
