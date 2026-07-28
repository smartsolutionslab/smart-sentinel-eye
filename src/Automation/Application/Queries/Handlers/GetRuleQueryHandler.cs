using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

public sealed class GetRuleQueryHandler(IRuleQuerySource rules)
    : IQueryHandler<GetRuleQuery, Result<RuleDto, GetRuleError>>
{
    public async Task<Result<RuleDto, GetRuleError>> HandleAsync(
        GetRuleQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        Rule? rule = await rules.Rules
            .SingleOrDefaultAsync(candidate => candidate.Name.Value == query.Name, cancellationToken);

        if (rule is null)
        {
            return Result<RuleDto, GetRuleError>.Failure(new GetRuleError.RuleNotFound(query.Name));
        }

        return Result<RuleDto, GetRuleError>.Success(RuleMapper.Map(rule));
    }
}
