namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// The first grid-invariant violation found by
/// <see cref="Layout.ValidateGrid"/> (spec 010, ADR-0112 §2). The
/// command handlers map each case to its <c>LAYOUT_GRID_*</c>
/// <c>400</c> error — an operator input error is a
/// <see cref="Shared.Kernel.Result{TValue,TError}"/> failure, not a
/// thrown exception (ADR-0047).
/// </summary>
public enum GridViolation
{
    /// <summary>A revision must carry at least one tile.</summary>
    Empty,

    /// <summary>Two tiles occupy the same <see cref="GridPosition"/>.</summary>
    DuplicatePosition,

    /// <summary>A tile sits outside the grid bounds.</summary>
    OutOfBounds,

    /// <summary>The grid or populated-tile count exceeds the max-tiles ceiling.</summary>
    TooLarge,
}
