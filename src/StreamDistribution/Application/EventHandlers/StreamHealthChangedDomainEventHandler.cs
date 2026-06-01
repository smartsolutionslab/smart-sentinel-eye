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
public sealed class StreamHealthChangedDomainEventHandler(IEventBus events)
    : IDomainEventHandler<StreamHealthChangedDomainEvent>
{
    public Task Handle(StreamHealthChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        StreamHealthChangedV1 @event = new(
            Camera: domainEvent.Camera.Value,
            FromState: domainEvent.FromState.Value,
            ToState: domainEvent.ToState.Value,
            ChangedAt: domainEvent.ChangedAt,
            Error: domainEvent.Error,
            Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.ChangedAt, null, null));
        return events.PublishAsync(@event, cancellationToken);
    }
}
