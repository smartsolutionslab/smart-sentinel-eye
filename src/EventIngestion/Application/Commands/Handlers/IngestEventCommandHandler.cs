using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// Single funnel for all ingress paths (MQTT subscriber + HTTP
/// manual + HTTP webhook). Persists the envelope, then the
/// aggregate's <c>EventIngestedDomainEvent</c> drives the
/// <c>FabEventIngestedV1</c> fan-out via
/// <see cref="EventHandlers.EventIngestedDomainEventHandler"/>.
/// </summary>
public sealed class IngestEventCommandHandler(
    IEventRepository events,
    IClock clock,
    EventTypeAdmission admission,
    ILogger<IngestEventCommandHandler> logger)
    : ICommandHandler<IngestEventCommand, Result<EventIdentifier, IngestEventError>>
{
    public async Task<Result<EventIdentifier, IngestEventError>> HandleAsync(
        IngestEventCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        EventEnvelope envelope = command.Envelope;

        // Hybrid-idempotency check (FR-002). The unique
        // (fab_id, event_id) constraint in Postgres is the durable
        // backstop; this round-trip avoids raising
        // FabEventIngestedV1 twice on retry.
        bool exists = await events
            .ExistsAsync(envelope.Fab, envelope.Identifier, cancellationToken);
        if (exists)
        {
            logger.IdempotentReDelivery(envelope.Identifier, envelope.Fab);
            return Failure(IngestEventFailures.EventAlreadyIngested(envelope.Identifier.Value));
        }

        // Spec 269 FR-007: redelivery outranks it (above), and future skew
        // outranks it (inside Build) — a strict source's admission verdict is
        // consulted last of the three.
        EventTypeVerdicts verdicts = await admission.AssessAsync([envelope], cancellationToken);

        Result<EventAggregate, IngestEventError> built = Build(envelope, verdicts);
        if (!built.IsSuccess)
        {
            return Failure(built.Error);
        }

        events.Add(built.Value);
        await events.SaveAsync(cancellationToken);

        // After the commit (spec 103 FR-006). The two early returns above are
        // what keeps a redelivery and a future-skew refusal out of the count,
        // so no branch is added here.
        IngestVolume.Record(envelope.Source);

        return Success(built.Value.Id);
    }

    /// <summary>
    /// Builds the aggregate, or the reason it cannot be built — future skew
    /// first, then the admission verdict (FR-007), mirroring
    /// <c>IngestEventBatchCommandHandler.Build</c>.
    /// </summary>
    private Result<EventAggregate, IngestEventError> Build(EventEnvelope envelope, EventTypeVerdicts verdicts)
    {
        EventAggregate @event;
        try
        {
            @event = EventAggregate.Ingest(
                envelope.Identifier,
                envelope.Fab,
                envelope.Source,
                envelope.Device,
                envelope.Kind,
                envelope.OccurredAt,
                envelope.Payload,
                clock);
        }
        catch (ArgumentException)
        {
            // Future-skew rule rejected (FR-014). Remap so the HTTP
            // layer can return a 400 with the typed code.
            return Failure(IngestEventFailures.OccurredAtTooFarInFuture(envelope.OccurredAt.Value));
        }

        if (verdicts.Refuses(envelope))
        {
            logger.UnregisteredEventTypeRefused(envelope.Identifier, envelope.Fab, envelope.Source, envelope.Kind);
            return Failure(IngestEventFailures.EventTypeNotRegistered(
                envelope.Fab.Value, envelope.Source.Value, envelope.Kind.Value));
        }

        return Success(@event);
    }
}
