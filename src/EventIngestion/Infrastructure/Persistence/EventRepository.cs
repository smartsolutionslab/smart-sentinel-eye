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
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<EventAggregate>.None : Option<EventAggregate>.Some(found);
    }

    public Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
        dbContext.Events.AnyAsync(eventEntity => eventEntity.Fab == fab && eventEntity.Id == identifier, cancellationToken);

    public void Add(EventAggregate @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        dbContext.Events.Add(@event);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        EventAggregate[] tracked = dbContext.ChangeTracker
            .Entries<EventAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (EventAggregate @event in tracked)
        {
            IDomainEvent[] events = @event.PendingEvents.ToArray();
            @event.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
