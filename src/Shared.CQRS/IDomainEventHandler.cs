using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// In-process handler for a domain event raised by an aggregate root. Per
/// ADR-0040 these handlers translate domain events to integration events
/// and publish them via <see cref="IEventBus"/>.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches the pending domain events on an aggregate to all registered
/// in-process handlers and clears the aggregate's buffer. Repositories call
/// this from their <c>SaveAsync</c> implementation after persistence.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
