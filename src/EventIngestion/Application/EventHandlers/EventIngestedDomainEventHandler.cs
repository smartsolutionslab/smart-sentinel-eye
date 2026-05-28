using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="EventIngestedDomainEvent"/>
/// into the V1 integration event on the bus (spec 006 FR-016). Per
/// ADR-0088 the publish rides Wolverine's Postgres outbox so it
/// commits with the persistence transaction.
/// </summary>
public sealed class EventIngestedDomainEventHandler(
    IEventBus events,
    ILogger<EventIngestedDomainEventHandler> log)
    : IDomainEventHandler<EventIngestedDomainEvent>
{
    public async Task Handle(EventIngestedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new FabEventIngestedV1(
                EventIdentifier: domainEvent.Identifier.Value,
                Fab: domainEvent.Fab.Value,
                Source: domainEvent.Source.Value,
                Device: domainEvent.Device.Value,
                Kind: domainEvent.Kind.Value,
                OccurredAt: domainEvent.OccurredAt.Value,
                IngestedAt: domainEvent.IngestedAt.Value,
                Payload: domainEvent.Payload.Value),
            cancellationToken).ConfigureAwait(false);

        log.LogDebug(
            "Published FabEventIngestedV1 for {Identifier} ({Source}/{Device}).",
            domainEvent.Identifier, domainEvent.Source, domainEvent.Device);
    }
}
