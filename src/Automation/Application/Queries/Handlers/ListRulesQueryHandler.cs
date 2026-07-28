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

        IQueryable<Rule> filtered = rules.Rules;

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
