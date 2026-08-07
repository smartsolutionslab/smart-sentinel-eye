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
        IQueryable<Rule> filtered = rules.Rules.Where(rule => scopedFabs.Contains(rule.Fab));

        if (!string.IsNullOrWhiteSpace(state))
        {
            RuleState parsedState;
            try
            {
                parsedState = RuleState.From(state);
            }
            catch (ArgumentException)
            {
                return Failure(ListRulesFailures.InvalidState(state));
            }

            filtered = filtered.Where(rule => rule.State == parsedState);
        }

        if (!string.IsNullOrWhiteSpace(triggerSource))
        {
            filtered = filtered.Where(rule => rule.TriggerSource == triggerSource);
        }

        if (!string.IsNullOrWhiteSpace(triggerKind))
        {
            filtered = filtered.Where(rule => rule.TriggerKind == triggerKind);
        }

        List<Rule> matches = await filtered
            .OrderByDescending(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RuleDto> projected = matches.Select(RuleMapper.Map).ToList();

        return Success(projected);
    }
}
