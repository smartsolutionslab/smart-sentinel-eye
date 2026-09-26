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
    // CA1720 fires because "Single" collides with System.Single. Suppressed
    // deliberately: this mirrors GridPosition's naming convention for a
    // grid's degenerate 1×1 case, and the tests name it exactly this way.
#pragma warning disable CA1720
    /// <summary>The 1×1 span every tile had before spec 258.</summary>
    public static readonly TileSpan Single = new(1, 1);
#pragma warning restore CA1720

    public static TileSpan From(int rows, int cols)
    {
        Ensure.That(rows).AtLeast(1);
        Ensure.That(cols).AtLeast(1);
        return new(rows, cols);
    }

    public override string ToString() => $"{Rows}x{Cols}";
}
