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
            return Result<RuleDto, GetRuleError>.Failure(new GetRuleError.RuleNotFound(query.Name));
        }

        Rule? rule = await rules.Rules
            .SingleOrDefaultAsync(candidate => candidate.Name == parsed, cancellationToken);

        if (rule is null)
        {
            return Result<RuleDto, GetRuleError>.Failure(new GetRuleError.RuleNotFound(query.Name));
        }

        return Result<RuleDto, GetRuleError>.Success(RuleMapper.Map(rule));
    }
}
