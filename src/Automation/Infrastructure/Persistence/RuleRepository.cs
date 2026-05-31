using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

public sealed class RuleRepository(
    AutomationDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IRuleRepository
{
    public async Task<Option<RuleAggregate>> GetByIdentifierAsync(
        RuleIdentifier rule, CancellationToken cancellationToken)
    {
        RuleAggregate? found = await dbContext.Rules
            .FirstOrDefaultAsync(candidate => candidate.Id == rule, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<RuleAggregate>.None : Option<RuleAggregate>.Some(found);
    }

    public async Task<Option<RuleAggregate>> GetByNameAsync(
        RuleName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        // FR-002: archived names are released for re-use; ignore Archived rows.
        RuleAggregate? found = await dbContext.Rules
            .Where(rule => rule.Name == name)
            .Where(rule => rule.State != RuleState.Archived)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<RuleAggregate>.None : Option<RuleAggregate>.Some(found);
    }

    public void Add(RuleAggregate rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        dbContext.Rules.Add(rule);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        RuleAggregate[] tracked = dbContext.ChangeTracker
            .Entries<RuleAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (RuleAggregate rule in tracked)
        {
            var events = rule.PendingEvents.ToArray();
            rule.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
