using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Evaluation;

/// <summary>
/// A rule plus its pre-parsed predicate + value expression. Built
/// once at rule-publish time and cached in <see cref="IRuleCache"/>.
/// Walking <see cref="CompiledPredicate"/> at evaluation time is
/// allocation-free per <c>AelInterpreter</c>.
/// </summary>
public sealed class CompiledRule
{
    public RuleIdentifier Identifier { get; }

    public string TriggerSource { get; }

    public string TriggerKind { get; }

    public DateTimeOffset CreatedAt { get; }

    public AelExpression CompiledPredicate { get; }

    public RuleAction Action { get; }

    /// <summary>
    /// Pre-parsed value expression for <see cref="RuleAction.SetVariableValue"/>
    /// actions. <c>null</c> for <see cref="RuleAction.HighlightOverlay"/>.
    /// </summary>
    public AelExpression? CompiledValueExpression { get; }

    private CompiledRule(
        RuleIdentifier identifier,
        string triggerSource,
        string triggerKind,
        DateTimeOffset createdAt,
        AelExpression compiledPredicate,
        RuleAction action,
        AelExpression? compiledValueExpression)
    {
        Identifier = identifier;
        TriggerSource = triggerSource;
        TriggerKind = triggerKind;
        CreatedAt = createdAt;
        CompiledPredicate = compiledPredicate;
        Action = action;
        CompiledValueExpression = compiledValueExpression;
    }

    public static CompiledRule From(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        AelExpression predicate = AelParser.Parse(rule.Predicate.Value);
        AelExpression? valueExpression = rule.Action is RuleAction.SetVariableValue sv
            ? AelParser.Parse(sv.ValueExpression)
            : null;
        return new CompiledRule(
            rule.Id, rule.TriggerSource, rule.TriggerKind,
            rule.CreatedAt, predicate, rule.Action, valueExpression);
    }
}
