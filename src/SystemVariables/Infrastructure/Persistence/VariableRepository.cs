using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

public sealed class VariableRepository(
    SystemVariablesDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IVariableRepository
{
    public async Task<Option<Variable>> GetByIdentifierAsync(
        VariableIdentifier variable, CancellationToken cancellationToken)
    {
        Variable? found = await dbContext.Variables
            .FirstOrDefaultAsync(candidate => candidate.Id == variable, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Variable>.None : Option<Variable>.Some(found);
    }

    public async Task<Option<Variable>> GetByNameAsync(
        VariableName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        // FR-005: archived names are released for re-use; only return non-Archived rows.
        Variable? found = await dbContext.Variables
            .Where(variable => variable.Name == name)
            .Where(variable => variable.State != VariableState.Archived)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Variable>.None : Option<Variable>.Some(found);
    }

    public void Add(Variable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        dbContext.Variables.Add(variable);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Variable[] tracked = dbContext.ChangeTracker
            .Entries<Variable>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Variable variable in tracked)
        {
            IDomainEvent[] events = variable.PendingEvents.ToArray();
            variable.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
