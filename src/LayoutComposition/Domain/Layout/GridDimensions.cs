using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// The row × column shape of a multi-tile layout grid (spec 010/258,
/// ADR-0112 §2/§4, ADR-0156 §1). The single source of truth for the
/// max-tiles ceiling: a wall is capped at <see cref="MaxCells"/> cells
/// (3×3) so the kiosk never decodes more than <see cref="MaxTiles"/>
/// simultaneous WHEP peers — the §IV latency mitigation. A 1×1 grid
/// (<see cref="Single"/>) is the migrated single-camera layout; 2×2
/// (<see cref="Default"/>) is the designer default.
///
/// <para>
/// ADR-0156 raises the cap from 4 to 9 as a ceiling to design toward, not
/// a guarantee: it is <b>not yet verified safe on kiosk hardware</b> — a
/// real-kiosk-hardware decode measurement is a precondition for shipping
/// this cap to production (spec 262 §4). Dev-machine figures are recorded
/// in <c>verification.md</c>, not a discharge of that gate.
/// </para>
/// </summary>
public sealed record GridDimensions(int Rows, int Cols) : IValueObject
{
    /// <summary>Maximum number of populated tiles on a wall (ADR-0156 §1).</summary>
    public const int MaxTiles = 9;

    /// <summary>Maximum number of grid cells (<c>Rows × Cols</c>) on a wall (ADR-0156 §1).</summary>
    public const int MaxCells = 9;

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

    /// <summary>
    /// True when the full rectangle a tile at <paramref name="position"/>
    /// with <paramref name="span"/> occupies fits inside this grid (spec 262,
    /// ADR-0156 §2) — not just its origin cell.
    ///
    /// <para>
    /// Written as subtraction, not <c>position.Row + span.Rows &lt;= Rows</c>
    /// (code review finding B1): <see cref="TileSpan"/> only guards its own
    /// lower bound, so a caller-supplied span up to <see cref="int.MaxValue"/>
    /// reaches here, and that addition wraps around to a negative number
    /// instead of overflowing past <see cref="Rows"/>/<see cref="Cols"/> —
    /// silently reporting an out-of-bounds rectangle as contained.
    /// <see cref="Contains(GridPosition)"/> already bounds <c>position.Row</c>
    /// to <c>[0, Rows)</c>, so <c>Rows - position.Row</c> cannot overflow.
    /// </para>
    /// </summary>
    public bool Contains(GridPosition position, TileSpan span) =>
        Contains(position) &&
        span.Rows <= Rows - position.Row &&
        span.Cols <= Cols - position.Col;

    public override string ToString() => $"{Rows}x{Cols}";
}
