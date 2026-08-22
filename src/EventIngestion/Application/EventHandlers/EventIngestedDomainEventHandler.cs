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
public sealed class EventIngestedDomainEventHandler(
    IEventBus events,
    IJourneyOrigin journeys,
    ILogger<EventIngestedDomainEventHandler> logger)
    : IDomainEventHandler<EventIngestedDomainEvent>
{
    /// <summary>
    /// What the journey is called wherever someone goes looking for it.
    /// </summary>
    private const string OriginName = "ingest plant-floor event";

    public async Task Handle(EventIngestedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (identifier, fab, source, device, kind, occurredAt, ingestedAt, payload) = domainEvent;

        // Spec 026. Ingestion publishes from a background service draining a
        // channel, where nothing is in progress — so without this the event is
        // published as an orphan and no downstream work can be traced back to
        // it. Everything after the publish already joins up on its own.
        //
        // Here rather than around the batch, and that is the whole of the
        // design: the dispatcher invokes handlers one domain event at a time,
        // so one journey per event falls out of the structure. A batch-level
        // origin is less code, produces a joined trace, and reads as correct
        // from the effect end while merging two hundred unrelated journeys
        // (FR-006).
        using IJourney journey = journeys.Begin(OriginName);

        try
        {
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
        }
        catch (Exception exception)
        {
            // Recorded, not handled. A journey that failed to begin otherwise
            // looks exactly like one that began and caused nothing — same name,
            // no children, no error — and those are opposite facts.
            journey.Failed(exception);
            throw;
        }

        logger.PublishedIntegrationEvent(identifier, source, device);
    }
}
