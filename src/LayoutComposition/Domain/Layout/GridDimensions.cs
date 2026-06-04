using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// The row × column shape of a multi-tile layout grid (spec 010,
/// ADR-0112 §2/§4). The single source of truth for the v1 max-tiles
/// ceiling: a wall is capped at <see cref="MaxCells"/> cells (2×2) so
/// the kiosk never decodes more than <see cref="MaxTiles"/> simultaneous
/// WHEP peers — the §IV latency mitigation. A 1×1 grid
/// (<see cref="Single"/>) is the migrated single-camera layout; 2×2
/// (<see cref="Default"/>) is the designer default.
/// </summary>
public sealed record GridDimensions(int Rows, int Cols) : IValueObject
{
    /// <summary>Maximum number of populated tiles on a wall (ADR-0112 §4).</summary>
    public const int MaxTiles = 4;

    /// <summary>Maximum number of grid cells (<c>Rows × Cols</c>) on a wall (ADR-0112 §4).</summary>
    public const int MaxCells = 4;

    /// <summary>Designer default — a 2×2 wall.</summary>
    public static readonly GridDimensions Default = new(2, 2);

    /// <summary>The N=1 / migrated single-camera layout shape (a 1×1 grid).</summary>
    public static readonly GridDimensions Cell = new(1, 1);

    public static GridDimensions From(int rows, int cols)
    {
        Ensure.That(rows).AtLeast(1);
        Ensure.That(cols).AtLeast(1);
        Ensure.That(rows * cols).Satisfies(
            cells => cells <= MaxCells,
            $"a grid may not exceed {MaxCells} cells.");
        return new(rows, cols);
    }

    /// <summary>True when <paramref name="position"/> is in-bounds for this grid.</summary>
    public bool Contains(GridPosition position) =>
        position.Row >= 0 && position.Row < Rows &&
        position.Col >= 0 && position.Col < Cols;

    public override string ToString() => $"{Rows}x{Cols}";
}
