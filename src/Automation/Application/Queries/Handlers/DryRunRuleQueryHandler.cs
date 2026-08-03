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
            parsed = RuleName.From(query.Name);
        }
        catch (ArgumentException)
        {
            return Failure(DryRunRuleFailures.RuleNotFound(query.Name));
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
        FabIdentifier[] fabs = [.. query.Fabs];
        List<Rule> matches = await rules.Rules
            .Where(candidate => fabs.Contains(candidate.Fab) && candidate.Name == parsed)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Failure(DryRunRuleFailures.RuleNotFound(query.Name));
        }

        if (matches.Count > 1)
        {
            return Failure(DryRunRuleFailures.FabAmbiguous(
                    query.Name, RuleFabCandidates.Describe(matches)));
        }

        Rule rule = matches[0];

        JsonDocument sample;
        try
        {
            sample = JsonDocument.Parse(query.SampleEvent ?? string.Empty);
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
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AelParseException)
            {
                return Failure(DryRunRuleFailures.EvaluationFailed(ex.Message));
            }
        }
    }
}
