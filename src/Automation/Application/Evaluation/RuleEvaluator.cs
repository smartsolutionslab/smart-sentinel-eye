using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;

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
        FabIdentifier fab, string triggerSource, string triggerKind, EvaluationContext context)
    {
        Ensure.That(fab).IsNotNull();

        // Scoped to the originating fab (spec 013 FR-002). Before this, an
        // event from one fab was matched against every fab's rules and the
        // resulting change was attributed to the ingesting fab (#1252).
        IReadOnlyList<CompiledRule> candidates = cache.LookupActive(fab, triggerSource, triggerKind);
        if (candidates.Count == 0)
        {
            return Array.Empty<RuleActionEffect>();
        }

        List<RuleActionEffect> effects = new(candidates.Count);
        foreach (CompiledRule rule in candidates)
        {
            if (!TryEvaluatePredicate(rule, context))
            {
                continue;
            }

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
                        highlight.Overlay.Value, highlight.Duration.Value));
                    break;

                default:
                    logger.UnhandledRuleActionCase(rule.Action.GetType().Name, rule.Identifier);
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
        // Not an enumerated list of exception types — that list missed
        // FormatException (#2427) and OverflowException. This guards one pure
        // synchronous function over in-memory data with no I/O to fail, and
        // nothing is swallowed: every exception here is logged with its rule
        // identifier. OperationCanceledException is excluded on principle,
        // not because it can reach here today (the guarded call threads no
        // CancellationToken) — carved out now so the next person who makes
        // something on this path async inherits the boundary already drawn,
        // rather than a cancelled request silently logged as "this rule failed".
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.PredicateEvaluationFailed(exception, rule.Identifier);
            return false;
        }
    }

    private bool TryEvaluateValueExpression(
        CompiledRule rule, EvaluationContext context, out string wireValue)
    {
        wireValue = string.Empty;
        if (rule.CompiledValueExpression is null)
        {
            return false;
        }

        try
        {
            AelValue result = AelInterpreter.Evaluate(rule.CompiledValueExpression, context);
            wireValue = result.ToWireString();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.ValueExpressionEvaluationFailed(exception, rule.Identifier);
            return false;
        }
    }
}
