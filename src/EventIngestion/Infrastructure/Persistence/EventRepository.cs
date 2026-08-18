using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class EventRepository(
    EventIngestionDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IEventRepository
{
    public async Task<Option<EventAggregate>> GetByIdentifierAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken)
    {
        EventAggregate? found = await dbContext.Events
            .Where(eventEntity => eventEntity.Fab == fab && eventEntity.Id == identifier)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<EventAggregate>.None : Option<EventAggregate>.Some(found);
    }

    public Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
        dbContext.Events.AnyAsync(eventEntity => eventEntity.Fab == fab && eventEntity.Id == identifier, cancellationToken);

    public async Task<IReadOnlySet<EventIdentifier>> ExistingAsync(
        IReadOnlyCollection<(FabIdentifier Fab, EventIdentifier Identifier)> candidates,
        CancellationToken cancellationToken)
    {
        Ensure.That(candidates).IsNotNull();

        if (candidates.Count == 0)
        {
            return new HashSet<EventIdentifier>();
        }

        // Both lists are passed so the fab predicate can prune partitions. It
        // is a superset test — a fab from one pair could in principle match an
        // identifier from another — but the identifiers are Guid v7, so a
        // false positive would need a collision, and the unique constraint is
        // still the durable backstop underneath (spec 006 FR-002).
        FabIdentifier[] fabs = [.. candidates.Select(candidate => candidate.Fab).Distinct()];
        EventIdentifier[] identifiers = [.. candidates.Select(candidate => candidate.Identifier)];

        EventIdentifier[] found = await dbContext.Events
            .Where(eventEntity =>
                fabs.Contains(eventEntity.Fab) && identifiers.Contains(eventEntity.Id))
            .Select(eventEntity => eventEntity.Id)
            .ToArrayAsync(cancellationToken);

        return found.ToHashSet();
    }

    public void Add(EventAggregate @event)
    {
        Ensure.That(@event).IsNotNull();
        dbContext.Events.Add(@event);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        EventAggregate[] tracked = dbContext.ChangeTracker
            .Entries<EventAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken);

        // Collected first and dispatched once. Dispatching per aggregate was
        // invisible while the caller saved one event at a time and becomes a
        // round trip per event the moment a batch is saved (spec 020 FR-010).
        List<IDomainEvent> pending = [];
        foreach (EventAggregate @event in tracked)
        {
            pending.AddRange(@event.PendingEvents);
            @event.ClearPendingEvents();
        }

        if (pending.Count > 0)
        {
            await domainEventDispatcher.DispatchAsync(pending, cancellationToken);
        }
    }
}
