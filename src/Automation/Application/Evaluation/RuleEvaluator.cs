using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Evaluation;

/// <summary>
/// Runs a single <see cref="EvaluationContext"/> through every
/// Active rule matching the trigger <c>(source, kind)</c> and
/// collects the resulting <see cref="RuleActionEffect"/>s in
/// declaration order (spec FR-012 — last write wins per
/// SystemVariable).
///
/// <para>
/// Runtime errors during predicate / action evaluation are logged
/// and skipped per rule — one bad rule does not stop the loop
/// (spec NFR-005 replay-safety + Karpathy guideline §6 trust-
/// boundary).
/// </para>
/// </summary>
public sealed class RuleEvaluator(
    IRuleCache cache,
    ILogger<RuleEvaluator> logger)
{
    public IReadOnlyList<RuleActionEffect> Evaluate(
        string triggerSource, string triggerKind, EvaluationContext context)
    {
        IReadOnlyList<CompiledRule> candidates = cache.LookupActive(triggerSource, triggerKind);
        if (candidates.Count == 0) return Array.Empty<RuleActionEffect>();

        List<RuleActionEffect> effects = new(candidates.Count);
        foreach (CompiledRule rule in candidates)
        {
            if (!TryEvaluatePredicate(rule, context)) continue;

            switch (rule.Action)
            {
                case RuleAction.SetVariableValue setValue:
                    if (TryEvaluateValueExpression(rule, context, out string? wireValue))
                    {
                        effects.Add(new RuleActionEffect.SetVariableValue(
                            setValue.VariableName, wireValue));
                    }
                    break;

                case RuleAction.HighlightOverlay highlight:
                    effects.Add(new RuleActionEffect.HighlightOverlay(
                        highlight.Overlay, highlight.DurationMs));
                    break;

                default:
                    Log.UnhandledRuleActionCase(logger, rule.Action.GetType().Name, rule.Identifier);
                    break;
            }
        }
        return effects;
    }

    private bool TryEvaluatePredicate(CompiledRule rule, EvaluationContext context)
    {
        try
        {
            AelValue result = AelInterpreter.Evaluate(rule.CompiledPredicate, context);
            return result is AelValue.BoolValue { Value: true };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Log.PredicateEvaluationFailed(logger, ex, rule.Identifier);
            return false;
        }
    }

    private bool TryEvaluateValueExpression(
        CompiledRule rule, EvaluationContext context, out string wireValue)
    {
        wireValue = string.Empty;
        if (rule.CompiledValueExpression is null) return false;
        try
        {
            AelValue result = AelInterpreter.Evaluate(rule.CompiledValueExpression, context);
            wireValue = result.ToWireString();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Log.ValueExpressionEvaluationFailed(logger, ex, rule.Identifier);
            return false;
        }
    }
}
