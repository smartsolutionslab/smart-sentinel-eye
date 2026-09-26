using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// The <c>(rows, cols)</c> extent a <see cref="Tile"/> claims from its
/// <see cref="GridPosition"/> origin (spec 258, ADR-0156 §2), mirroring
/// <see cref="GridPosition"/>'s shape. Only the lower bound (≥1 on each
/// axis) is guarded here — a span is meaningless without its grid, so
/// the upper bound (in-bounds against <see cref="GridDimensions"/>) is
/// validated by the owning <see cref="Layout"/> aggregate, not this
/// value object.
/// </summary>
public sealed record TileSpan(int Rows, int Cols) : IValueObject
{
    /// <summary>
    /// The 1×1 span every tile had before spec 258. Named <c>Cell</c>, not
    /// <c>Single</c> — the same CA1720 (collides with <see cref="float"/>)
    /// that <see cref="GridDimensions.Cell"/> is named to avoid.
    /// </summary>
    public static readonly TileSpan Cell = new(1, 1);

    public static TileSpan From(int rows, int cols)
    {
        Ensure.That(rows).AtLeast(1);
        Ensure.That(cols).AtLeast(1);
        return new(rows, cols);
    }

    public override string ToString() => $"{Rows}x{Cols}";
}
