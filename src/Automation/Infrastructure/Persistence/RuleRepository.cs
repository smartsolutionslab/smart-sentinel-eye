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
            .FirstOrDefaultAsync(candidate => candidate.Id == rule, cancellationToken);
        return found is null ? Option<RuleAggregate>.None : Option<RuleAggregate>.Some(found);
    }

    public async Task<Option<RuleAggregate>> GetByNameAsync(
        FabIdentifier fab, RuleName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        // FR-002: archived names are released for re-use; ignore Archived rows.
        // Fab first: a name is unique only within one (spec 013), so without
        // it this could return another fab's rule.
        RuleAggregate? found = await dbContext.Rules
            .Where(rule => rule.Fab == fab)
            .Where(rule => rule.Name == name)
            .Where(rule => rule.State != RuleState.Archived)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<RuleAggregate>.None : Option<RuleAggregate>.Some(found);
    }

    public void Add(RuleAggregate rule)
    {
        Ensure.That(rule).IsNotNull();
        dbContext.Rules.Add(rule);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        RuleAggregate[] tracked = dbContext.ChangeTracker
            .Entries<RuleAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (RuleAggregate rule in tracked)
        {
            IDomainEvent[] events = rule.PendingEvents.ToArray();
            rule.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }
}
