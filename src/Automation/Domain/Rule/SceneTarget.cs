namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// What a <see cref="RuleAction.SwitchWallScene"/> asks the wall to show
/// next (spec 296 plan.md §2.1): either the next scene in the wall's
/// ordered set, or a named layout. Mirrors
/// <c>LayoutComposition.Domain.Wall.SceneTarget</c>'s two-variant shape,
/// deliberately declared again here rather than shared — §III forbids a
/// cross-context Domain reference, and the two contexts' aggregates reach
/// this type through different VOs (<see cref="LayoutIdentifier"/> here,
/// LayoutComposition's own there).
/// </summary>
public abstract record SceneTarget
{
    /// <summary>
    /// Wire literal for <see cref="Next"/> — Domain's own spelling, owned
    /// independently of any integration event (ADR-0040). It matches
    /// <c>WallSceneSwitchRequestedV1.NextTarget</c> by agreement, not by
    /// any runtime mapping; <c>FabEventIngestedV1HandlerTests</c> and
    /// <c>RuleEvaluatorTests</c> pin the producer side against that literal,
    /// so a rename here surfaces as a test failure rather than silent drift.
    /// </summary>
    public const string NextLiteral = "Next";

    /// <summary>Wire literal for <see cref="Layout"/>, same reasoning as <see cref="NextLiteral"/>.</summary>
    public const string LayoutLiteral = "Layout";

    private SceneTarget() { }

    public sealed record Next : SceneTarget;

    public sealed record Layout(LayoutIdentifier Value) : SceneTarget;
}
