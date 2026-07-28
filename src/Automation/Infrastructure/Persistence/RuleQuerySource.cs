using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

/// <summary>
/// Read-side seam (spec 007 T059): hands query handlers an EF Core
/// <see cref="IQueryable{T}"/> over the rules table. <c>AsNoTracking</c> —
/// nothing on the read path mutates a rule.
/// </summary>
public sealed class RuleQuerySource(AutomationDbContext dbContext) : IRuleQuerySource
{
    public IQueryable<Rule> Rules => dbContext.Rules.AsNoTracking();
}
