using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

public sealed class VariableRepository(
    SystemVariablesDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IVariableRepository
{
    public async Task<Option<Variable>> GetByIdentifierAsync(
        VariableIdentifier variable, CancellationToken cancellationToken)
    {
        Variable? found = await dbContext.Variables
            .FirstOrDefaultAsync(candidate => candidate.Id == variable, cancellationToken);
        return found is null ? Option<Variable>.None : Option<Variable>.Some(found);
    }

    public async Task<Option<Variable>> GetByNameAsync(
        FabIdentifier fab, VariableName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        // FR-005: archived names are released for re-use; only return non-Archived rows.
        Variable? found = await dbContext.Variables
            .Where(variable => variable.Fab == fab)
            .Where(variable => variable.Name == name)
            .Where(variable => variable.State != VariableState.Archived)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<Variable>.None : Option<Variable>.Some(found);
    }

    public void Add(Variable variable)
    {
        Ensure.That(variable).IsNotNull();
        dbContext.Variables.Add(variable);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Variable[] tracked = dbContext.ChangeTracker
            .Entries<Variable>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first, then commit the rows and the messages together (spec
        // 021 FR-001). The handler behind this one is the only one of the twelve
        // that reads as well as publishes, which is why this repository landed
        // on its own rather than among the seven identical ones.
        //
        // It is safe, and not by luck: the changed variable's value comes from
        // the domain event, not from a query, and the siblings it looks up are
        // not written by this transaction. Where a sibling ever were, the read
        // goes through the same DbContext and would see the pending change
        // rather than a stale one.
        foreach (Variable variable in tracked)
        {
            IDomainEvent[] events = variable.PendingEvents.ToArray();
            variable.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
