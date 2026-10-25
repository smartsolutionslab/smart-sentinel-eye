using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

public sealed class ListRulesQueryHandler(IRuleQuerySource rules)
    : IQueryHandler<ListRulesQuery, Result<IReadOnlyList<RuleDto>, ListRulesError>>
{
    public async Task<Result<IReadOnlyList<RuleDto>, ListRulesError>> HandleAsync(
        ListRulesQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (fabs, state, triggerSource, triggerKind) = query;

        // Scoped to the caller's fabs before any user-supplied filter, so a
        // filter can only ever narrow what they were already entitled to see
        // (spec 013 FR-005). An empty set yields nothing — an operator
        // assigned to no fab sees no rules, not every rule.
        // Value object, not .Value: Fab is value-converted, and a member
        // access on it throws at EF translation time rather than filtering.
        FabIdentifier[] scopedFabs = [.. fabs];
        var scoped = rules.Rules.Where(rule => scopedFabs.Contains(rule.Fab));

        var byState = FilterByState(scoped, state);
        if (byState.IsFailure)
        {
            return Failure(byState.Error);
        }

        var byTrigger = FilterByTrigger(byState.Value, triggerSource, triggerKind);
        if (!byTrigger.HasValue)
        {
            return Success<IReadOnlyList<RuleDto>>([]);
        }

        var matches = await byTrigger.Value
            .OrderByDescending(rule => rule.Creation.At)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RuleDto> projected = matches.Select(RuleMappings.ToDto).ToList();

        return Success(projected);
    }

    private static Result<IQueryable<Rule>, ListRulesError> FilterByState(IQueryable<Rule> filtered, string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return Success(filtered);
        }

        RuleState parsedState;
        try
        {
            parsedState = RuleState.From(state);
        }
        catch (ArgumentException)
        {
            return Failure(ListRulesFailures.InvalidState(state));
        }

        return Success(filtered.Where(rule => rule.State == parsedState));
    }

    private static Option<IQueryable<Rule>> FilterByTrigger(
        IQueryable<Rule> filtered,
        string? triggerSource,
        string? triggerKind)
    {
        var bySource = FilterBySource(filtered, triggerSource);
        return bySource.HasValue ? FilterByKind(bySource.Value, triggerKind) : bySource;
    }

    private static Option<IQueryable<Rule>> FilterBySource(IQueryable<Rule> filtered, string? triggerSource)
    {
        if (string.IsNullOrWhiteSpace(triggerSource))
        {
            return Option<IQueryable<Rule>>.Some(filtered);
        }

        // A filter value that could never be a TriggerSource matches nothing —
        // which is exactly what comparing it to the column did while this
        // property was a string. Parsing and failing the request instead would
        // turn a 200 with no rows into a 400, and the contract for this feature
        // is that no status code moves.
        TriggerSource parsedTriggerSource;
        try
        {
            parsedTriggerSource = TriggerSource.From(triggerSource);
        }
        catch (ArgumentException)
        {
            return Option<IQueryable<Rule>>.None;
        }

        return Option<IQueryable<Rule>>.Some(filtered.Where(rule => rule.TriggerSource == parsedTriggerSource));
    }

    private static Option<IQueryable<Rule>> FilterByKind(IQueryable<Rule> filtered, string? triggerKind)
    {
        if (string.IsNullOrWhiteSpace(triggerKind))
        {
            return Option<IQueryable<Rule>>.Some(filtered);
        }

        TriggerKind parsedTriggerKind;
        try
        {
            parsedTriggerKind = TriggerKind.From(triggerKind);
        }
        catch (ArgumentException)
        {
            return Option<IQueryable<Rule>>.None;
        }

        return Option<IQueryable<Rule>>.Some(filtered.Where(rule => rule.TriggerKind == parsedTriggerKind));
    }
}
