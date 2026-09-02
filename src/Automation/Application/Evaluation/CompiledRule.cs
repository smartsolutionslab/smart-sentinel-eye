using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;

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

    /// <summary>
    /// The fab this rule belongs to. Carried into the cache so lookup can be
    /// keyed on it — without it, an event from one fab matched every fab's
    /// rules (#1252).
    /// </summary>
    public FabIdentifier Fab { get; }

    public TriggerSource TriggerSource { get; }

    public TriggerKind TriggerKind { get; }

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
        FabIdentifier fab,
        TriggerSource triggerSource,
        TriggerKind triggerKind,
        DateTimeOffset createdAt,
        AelExpression compiledPredicate,
        RuleAction action,
        AelExpression? compiledValueExpression)
    {
        Identifier = identifier;
        Fab = fab;
        TriggerSource = triggerSource;
        TriggerKind = triggerKind;
        CreatedAt = createdAt;
        CompiledPredicate = compiledPredicate;
        Action = action;
        CompiledValueExpression = compiledValueExpression;
    }

    public static CompiledRule From(Rule rule)
    {
        Ensure.That(rule).IsNotNull();
        AelExpression predicate = AelParser.Parse(rule.Predicate.Value);
        AelExpression? valueExpression = rule.Action is RuleAction.SetVariableValue setVariableValue
            ? AelParser.Parse(setVariableValue.ValueExpression)
            : null;
        return new CompiledRule(
            rule.Id, rule.Fab, rule.TriggerSource, rule.TriggerKind,
            rule.Creation.At, predicate, rule.Action, valueExpression);
    }
}
