using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

/// <summary>
/// Compiles the stored rule and evaluates it against a caller-supplied sample
/// event. Deliberately bypasses <c>IRuleCache</c> and goes to the rule as
/// persisted, so a Draft rule — which is never in the cache — can be tried
/// before it is published. Nothing is written and no integration event is
/// raised.
/// </summary>
public sealed class DryRunRuleQueryHandler(IRuleQuerySource rules)
    : IQueryHandler<DryRunRuleQuery, Result<DryRunResultDto, DryRunRuleError>>
{
    public async Task<Result<DryRunResultDto, DryRunRuleError>> HandleAsync(
        DryRunRuleQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (fabs, name, sampleEvent) = query;

        // Compare the value object, not its inner string. RuleName is mapped
        // with a value conversion (RuleConfiguration), which EF can translate
        // for the whole property but not for a member access on it — reaching
        // into .Value threw at translation time, before the query ever ran.
        //
        // A name that is not a legal RuleName cannot match a stored row, so it
        // is not-found rather than a 500.
        RuleName parsed;
        try
        {
            parsed = RuleName.From(name);
        }
        catch (ArgumentException)
        {
            return Failure(DryRunRuleFailures.RuleNotFound(name));
        }

        // Fab-scoped like the reads (spec 013 FR-006): a trial run must not
        // be usable as a side channel to discover how another fab's rule
        // behaves. Still carries no If-Match — it persists nothing, and spec
        // 012 T048 pinned that with a test.
        //
        // Compare the value object, not its inner string — same trap as the
        // RuleName comparison above. Fab is value-converted, so reaching into
        // .Value throws at translation time and surfaces as a 500.
        //
        // A list, not SingleOrDefaultAsync — same reason as GetRuleQueryHandler:
        // per-fab uniqueness lets a multi-fab caller match the same name twice,
        // and the catch further down guards only the evaluation block, so a
        // Single throw would escape as a 500.
        //
        // Archived excluded: same reason as GetRuleQueryHandler — FR-002
        // releases an archived name for re-use, and dry-running an archived
        // rule is meaningless anyway (FR-004: only Active rules are evaluated).
        // Value object, not .Value: RuleState is value-converted, same trap as
        // RuleName and Fab above.
        FabIdentifier[] scopedFabs = [.. fabs];
        List<Rule> matches = await rules.Rules
            .Where(candidate => scopedFabs.Contains(candidate.Fab)
                && candidate.Name == parsed
                && candidate.State != RuleState.Archived)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Failure(DryRunRuleFailures.RuleNotFound(name));
        }

        // Keyed on distinct fabs, not match count — same reason as
        // GetRuleQueryHandler.
        IReadOnlyList<string> fabsHolding = RuleFabCandidates.Fabs(matches);
        if (fabsHolding.Count > 1)
        {
            return Failure(DryRunRuleFailures.FabAmbiguous(name, fabsHolding));
        }

        Rule rule = matches[0];

        JsonDocument sample;
        try
        {
            sample = JsonDocument.Parse(sampleEvent ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return Failure(DryRunRuleFailures.SampleEventNotJson(ex.Message));
        }

        using (sample)
        {
            EvaluationContext context = new(sample.RootElement);
            CompiledRule compiled = CompiledRule.From(rule);

            try
            {
                AelValue verdict = AelInterpreter.Evaluate(compiled.CompiledPredicate, context);

                // Same truthiness rule as RuleEvaluator — a non-boolean result
                // does NOT match. A dry run that disagreed with the live
                // pipeline would be worse than no dry run at all.
                if (verdict is not AelValue.BoolValue { Value: true })
                {
                    return Success(
                        new DryRunResultDto(Matched: false, EvaluatedValue: null));
                }

                // Only SetVariableValue produces a value; HighlightOverlay
                // matches but has nothing to evaluate.
                string? evaluated = compiled.CompiledValueExpression is null
                    ? null
                    : AelInterpreter.Evaluate(compiled.CompiledValueExpression, context).ToWireString();

                return Success(
                    new DryRunResultDto(Matched: true, EvaluatedValue: evaluated));
            }
            // Widened to match RuleEvaluator's filter: a dry run that disagreed
            // with the live pipeline would be worse than no dry run at all, and
            // the live pipeline now handles FormatException/OverflowException
            // instead of dead-lettering.
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(DryRunRuleFailures.EvaluationFailed(exception.Message));
            }
        }
    }
}
