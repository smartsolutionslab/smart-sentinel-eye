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
            return Result<DryRunResultDto, DryRunRuleError>.Failure(new DryRunRuleError.RuleNotFound(query.Name));
        }

        // Fab-scoped like the reads (spec 013 FR-006): a trial run must not
        // be usable as a side channel to discover how another fab's rule
        // behaves. Still carries no If-Match — it persists nothing, and spec
        // 012 T048 pinned that with a test.
        string[] fabs = [.. query.Fabs.Select(fab => fab.Value)];
        Rule? rule = await rules.Rules
            .Where(candidate => fabs.Contains(candidate.Fab.Value))
            .SingleOrDefaultAsync(candidate => candidate.Name == parsed, cancellationToken);

        if (rule is null)
        {
            return Result<DryRunResultDto, DryRunRuleError>.Failure(
                new DryRunRuleError.RuleNotFound(query.Name));
        }

        JsonDocument sample;
        try
        {
            sample = JsonDocument.Parse(query.SampleEvent ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return Result<DryRunResultDto, DryRunRuleError>.Failure(
                new DryRunRuleError.SampleEventNotJson(ex.Message));
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
                    return Result<DryRunResultDto, DryRunRuleError>.Success(
                        new DryRunResultDto(Matched: false, EvaluatedValue: null));
                }

                // Only SetVariableValue produces a value; HighlightOverlay
                // matches but has nothing to evaluate.
                string? evaluated = compiled.CompiledValueExpression is null
                    ? null
                    : AelInterpreter.Evaluate(compiled.CompiledValueExpression, context).ToWireString();

                return Result<DryRunResultDto, DryRunRuleError>.Success(
                    new DryRunResultDto(Matched: true, EvaluatedValue: evaluated));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AelParseException)
            {
                return Result<DryRunResultDto, DryRunRuleError>.Failure(
                    new DryRunRuleError.EvaluationFailed(ex.Message));
            }
        }
    }
}
