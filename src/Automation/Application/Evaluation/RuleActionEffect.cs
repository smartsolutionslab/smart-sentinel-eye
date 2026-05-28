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
}
