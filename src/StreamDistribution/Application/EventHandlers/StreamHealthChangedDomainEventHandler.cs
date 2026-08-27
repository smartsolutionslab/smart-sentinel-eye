using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="StreamHealthChangedDomainEvent"/>
/// into the cross-context <see cref="StreamHealthChangedV1"/> integration
/// event and publishes via the Wolverine outbox (ADR-0040 + ADR-0088).
/// </summary>
public sealed class StreamHealthChangedDomainEventHandler(IEventBus events, IJourneyOrigin journeys)
    : IDomainEventHandler<StreamHealthChangedDomainEvent>
{
    /// <summary>
    /// What the journey is called wherever someone goes looking for it.
    /// </summary>
    private const string OriginName = "observe stream health change";

    public async Task Handle(StreamHealthChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        // Spec 027. The health watcher publishes from a background loop, where
        // nothing is in progress — so without this the announcement is an orphan
        // and audit-observability's record of it cannot be traced back to the
        // check that made it.
        //
        // Here rather than in StreamHealthWatcher.PollOnceAsync, and the loop is
        // wrong for two separate reasons. It would merge every camera in a sweep
        // onto one origin. And it would begin a journey for every camera on
        // every poll, because the loop calls the command handler unconditionally
        // and only the aggregate knows whether anything changed — Stream.cs
        // guards each raise with `previous != State`. Sitting here, one journey
        // per real change falls out of the structure (FR-003, FR-006).
        //
        // Audit retention takes the opposite placement from the same rule: it
        // has no domain event handler, so its journey goes inside its loop. One
        // journey per announcement, both times.
        using IJourney journey = journeys.Begin(OriginName);

        StreamHealthChangedV1 @event = new(
            Camera: domainEvent.Camera.Value,
            FromState: domainEvent.FromState.Value,
            ToState: domainEvent.ToState.Value,
            ChangedAt: domainEvent.ChangedAt,
            Error: domainEvent.Error,
            // The fab comes off the domain event, not from ambient context:
            // the watcher publishes from a background loop where there is none.
            // Null only when the stream itself has no fab yet (spec 016).
            Metadata: new EventMetadata(
                Guid.CreateVersion7(),
                domainEvent.ChangedAt,
                domainEvent.Fab?.Value,
                null));

        try
        {
            await events.PublishAsync(@event, cancellationToken);
        }
        catch (Exception exception)
        {
            // Recorded, not handled. A journey that failed to begin otherwise
            // looks exactly like one that began and caused nothing.
            journey.Failed(exception);
            throw;
        }
    }
}
