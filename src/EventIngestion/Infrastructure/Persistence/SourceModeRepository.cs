using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class SourceModeRepository(
    EventIngestionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : ISourceModeRepository
{
    public async Task<Option<SourceMode>> GetAsync(
        FabIdentifier fab, Source source, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(source).IsNotNull();

        SourceMode? found = await dbContext.SourceModes
            .Where(sourceMode => sourceMode.Fab == fab && sourceMode.Source == source)
            .FirstOrDefaultAsync(cancellationToken);

        return found is null ? Option<SourceMode>.None : Option<SourceMode>.Some(found);
    }

    public void Add(SourceMode sourceMode)
    {
        Ensure.That(sourceMode).IsNotNull();
        dbContext.SourceModes.Add(sourceMode);
    }

    /// <summary>Spec 317, #2325, FR-013: undeclared means no row.</summary>
    public void Remove(SourceMode sourceMode)
    {
        Ensure.That(sourceMode).IsNotNull();
        dbContext.SourceModes.Remove(sourceMode);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        // Entries<SourceMode>() over the change tracker, not a list collected
        // before the removal above: a removed entity is still tracked (state
        // Deleted) and still carries PendingEvents, so its
        // SourceModeUndeclaredDomainEvent is dispatched the same way a
        // Declare's or Change's is.
        SourceMode[] tracked = dbContext.ChangeTracker
            .Entries<SourceMode>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first, same as RegisteredEventTypeRepository: the outbox
        // capture and the row commit below happen in one transaction. Nothing
        // subscribes to either domain event this aggregate raises (FR-014),
        // so this loop dispatches to no handler.
        foreach (SourceMode sourceMode in tracked)
        {
            IDomainEvent[] events = sourceMode.PendingEvents.ToArray();
            sourceMode.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
