using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="EventIngestedDomainEvent"/>
/// into the V1 integration event on the bus (spec 006 FR-016). Per
/// ADR-0088 the publish rides Wolverine's Postgres outbox so it
/// commits with the persistence transaction.
/// </summary>
public sealed class EventIngestedDomainEventHandler(IEventBus events, ILogger<EventIngestedDomainEventHandler> logger)
    : IDomainEventHandler<EventIngestedDomainEvent>
{
    public async Task Handle(EventIngestedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (identifier, fab, source, device, kind, occurredAt, ingestedAt, payload) = domainEvent;

        await events.PublishAsync(
            new FabEventIngestedV1(
                EventIdentifier: identifier.Value,
                Fab: fab.Value,
                Source: source.Value,
                Device: device.Value,
                Kind: kind.Value,
                OccurredAt: occurredAt.Value,
                IngestedAt: ingestedAt.Value,
                Payload: payload.Value,
                Metadata: new EventMetadata(identifier.Value, occurredAt.Value, fab.Value, null)),
            cancellationToken);

        logger.PublishedIntegrationEvent(identifier, source, device);
    }
}
