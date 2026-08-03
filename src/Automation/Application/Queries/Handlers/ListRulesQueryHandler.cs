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

        // Scoped to the caller's fabs before any user-supplied filter, so a
        // filter can only ever narrow what they were already entitled to see
        // (spec 013 FR-005). An empty set yields nothing — an operator
        // assigned to no fab sees no rules, not every rule.
        // Value object, not .Value: Fab is value-converted, and a member
        // access on it throws at EF translation time rather than filtering.
        FabIdentifier[] fabs = [.. query.Fabs];
        IQueryable<Rule> filtered = rules.Rules.Where(rule => fabs.Contains(rule.Fab));

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            RuleState state;
            try
            {
                state = RuleState.From(query.State);
            }
            catch (ArgumentException)
            {
                return Result<IReadOnlyList<RuleDto>, ListRulesError>.Failure(
                    new ListRulesError.InvalidState(query.State));
            }

            filtered = filtered.Where(rule => rule.State == state);
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerSource))
        {
            filtered = filtered.Where(rule => rule.TriggerSource == query.TriggerSource);
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerKind))
        {
            filtered = filtered.Where(rule => rule.TriggerKind == query.TriggerKind);
        }

        List<Rule> matches = await filtered
            .OrderByDescending(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RuleDto> projected = matches.Select(RuleMapper.Map).ToList();

        return Result<IReadOnlyList<RuleDto>, ListRulesError>.Success(projected);
    }
}
