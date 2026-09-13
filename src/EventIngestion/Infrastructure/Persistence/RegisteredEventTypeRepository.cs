using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class RegisteredEventTypeRepository(
    EventIngestionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IRegisteredEventTypeRepository
{
    public async Task<Option<RegisteredEventType>> GetRegisteredAsync(
        FabIdentifier fab, Kind kind, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(kind).IsNotNull();

        RegisteredEventType? found = await dbContext.RegisteredEventTypes
            .Where(eventType => eventType.Fab == fab && eventType.Kind == kind
                && eventType.State == RegistrationState.Registered)
            .FirstOrDefaultAsync(cancellationToken);

        return found is null ? Option<RegisteredEventType>.None : Option<RegisteredEventType>.Some(found);
    }

    public void Add(RegisteredEventType eventType)
    {
        Ensure.That(eventType).IsNotNull();
        dbContext.RegisteredEventTypes.Add(eventType);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        RegisteredEventType[] tracked = dbContext.ChangeTracker
            .Entries<RegisteredEventType>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first, same as WebhookIntegrationRepository: the outbox
        // capture and the row commit below happen in one transaction. Nothing
        // subscribes to either domain event this aggregate raises (FR-012),
        // so this loop dispatches to no handler — the same position
        // WebhookIntegration's register/revoke are already in.
        foreach (RegisteredEventType eventType in tracked)
        {
            IDomainEvent[] events = eventType.PendingEvents.ToArray();
            eventType.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
