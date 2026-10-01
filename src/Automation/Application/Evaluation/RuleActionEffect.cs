namespace SmartSentinelEye.Automation.Application.Evaluation;

/// <summary>
/// In-memory result of evaluating an Active rule against a single
/// event. The <see cref="EventHandlers.FabEventIngestedV1Handler"/>
/// publishes one V1 integration event per effect.
/// </summary>
public abstract record RuleActionEffect
{
    public sealed record SetVariableValue(string Name, string Value) : RuleActionEffect;

    public sealed record HighlightOverlay(Guid Overlay, int DurationMs) : RuleActionEffect;

    /// <summary>
    /// Spec 296 FR-004. <see cref="Rule"/> carries the firing rule's
    /// identifier — unlike the other two effects, which carry none —
    /// because the request this becomes has to name the rule that caused
    /// it (US2-2).
    /// </summary>
    public sealed record SwitchWallScene(Guid Wall, string Target, Guid? Layout, Guid Rule) : RuleActionEffect;
}
