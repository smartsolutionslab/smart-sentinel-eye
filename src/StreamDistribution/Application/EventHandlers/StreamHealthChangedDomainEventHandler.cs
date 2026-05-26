using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="StreamHealthChangedDomainEvent"/>
/// into the cross-context <see cref="StreamHealthChangedV1"/> integration
/// event and publishes via the Wolverine outbox (ADR-0040 + ADR-0088).
/// </summary>
public sealed class StreamHealthChangedDomainEventHandler(IEventBus events)
{
    public Task Handle(StreamHealthChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return events.PublishAsync(
            new StreamHealthChangedV1(
                Camera: domainEvent.Camera.Value,
                FromState: domainEvent.FromState.Value,
                ToState: domainEvent.ToState.Value,
                ChangedAt: domainEvent.ChangedAt,
                Error: domainEvent.Error.HasValue ? domainEvent.Error.Value : null),
            cancellationToken);
    }
}
