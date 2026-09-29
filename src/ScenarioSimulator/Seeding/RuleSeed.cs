namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Everything <see cref="AutomationRulesClient.EnsureRuleAsync"/> needs to
/// create and publish one rule, independent of which scenario-file construct
/// produced it (spec 289 plan.md §3.4). <see cref="HighlightRuleSeed"/> builds
/// one from the legacy <c>Highlight</c> field; a future <c>ReactionRuleSeed</c>
/// will build one from a declared <c>Reaction</c>.
/// </summary>
internal sealed record RuleSeed(string Name, string TriggerSource, string TriggerKind, string Predicate, RuleSeedAction Action);

/// <summary>The rule's action, on the wire as <c>ActionType</c> plus its own fields.</summary>
internal abstract record RuleSeedAction
{
    /// <summary>Flashes <paramref name="Overlay"/> for <paramref name="DurationMs"/>.</summary>
    internal sealed record HighlightOverlay(Guid Overlay, int DurationMs) : RuleSeedAction;

    /// <summary>Writes <paramref name="ValueExpression"/> into <paramref name="VariableName"/>.</summary>
    internal sealed record SetVariableValue(string VariableName, string ValueExpression) : RuleSeedAction;
}
