namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// The first scene-set invariant violation found by
/// <see cref="Wall.ValidateScenes"/> (spec 258 FR-002, PD-5). Mirrors
/// <c>GridViolation</c>'s role: the command handlers map each case to its
/// <c>WALL_*</c> <c>400</c> error, and the aggregate enforces the same check
/// on itself as a programmer-error backstop.
/// </summary>
public enum SceneSetViolation
{
    /// <summary>Fewer than <see cref="Wall.MinScenes"/> scenes (PD-5).</summary>
    TooFew,

    /// <summary>More than <see cref="Wall.MaxScenes"/> scenes (PD-5).</summary>
    TooMany,

    /// <summary>The same <c>LayoutIdentifier</c> appears twice in the ordered set.</summary>
    Duplicate,
}
