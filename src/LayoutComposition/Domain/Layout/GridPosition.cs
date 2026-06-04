using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// The zero-indexed <c>(row, col)</c> coordinate of a tile within a
/// layout grid (spec 010, ADR-0112 §2). Only the lower bound is guarded
/// here — a position is meaningless without its grid, so the upper bound
/// (in-bounds against <see cref="GridDimensions"/>) is validated by the
/// owning <see cref="Layout"/> aggregate, not this value object.
/// </summary>
public sealed record GridPosition(int Row, int Col) : IValueObject
{
    public static GridPosition From(int row, int col)
    {
        Ensure.That(row).AtLeast(0);
        Ensure.That(col).AtLeast(0);
        return new(row, col);
    }

    public override string ToString() => $"({Row},{Col})";
}
