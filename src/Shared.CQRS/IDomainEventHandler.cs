using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// In-process handler for a domain event raised by an aggregate root. Per
/// ADR-0040 these handlers translate domain events to integration events
/// and publish them via <see cref="IEventBus"/>.
///
/// <para>
/// <b>A handler runs before its write is committed</b> (spec 021). That is what
/// lets the integration event be captured inside the write's transaction, so
/// the row and the announcement share a fate — but it puts two constraints on
/// anything written here, and neither can be checked by a compiler:
/// </para>
///
/// <para>
/// <b>Publish, and do nothing else.</b> The write may still be rolled back. A
/// handler that calls another service, writes to a second store, or touches
/// anything outside this transaction will have acted on something that did not
/// happen.
/// </para>
///
/// <para>
/// <b>A throw here fails the write.</b> It no longer leaves the row committed
/// and the announcement missing — it aborts both, and the caller is told the
/// write failed. That is the correct trade while handlers only publish, and the
/// wrong one the moment a handler does real work that is allowed to fail.
/// </para>
///
/// <para>
/// Reading is permitted and one handler does it
/// (<c>VariableValueChangedDomainEventHandler</c>): it reads through the same
/// <c>DbContext</c>, so it sees the pending write rather than a stale row.
/// </para>
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches the pending domain events on an aggregate to all registered
/// in-process handlers and clears the aggregate's buffer.
///
/// <para>
/// Repositories call this from their <c>SaveAsync</c> <b>before</b> committing,
/// then commit through <see cref="ITransactionalCommit"/> so the rows and the
/// integration events the handlers published land in one transaction. It used
/// to be called after persistence, and the gap between the two is where an
/// integration event went missing (spec 021, issue #1605).
/// </para>
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
