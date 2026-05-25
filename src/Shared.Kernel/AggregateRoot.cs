using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Aggregate root base per ADR-0045 + ADR-0043. Carries the version field for
/// optimistic concurrency and the in-flight domain-event list.
/// </summary>
public abstract class AggregateRoot<TIdentifier>
    where TIdentifier : struct, IStronglyTypedId<Guid>
{
    private readonly List<IDomainEvent> _pendingEvents = new();

    public TIdentifier Id { get; protected set; }

    public int Version { get; protected set; }

    public IReadOnlyList<IDomainEvent> PendingEvents => _pendingEvents;

    protected void Raise(IDomainEvent domainEvent) => _pendingEvents.Add(domainEvent);

    public void ClearPendingEvents() => _pendingEvents.Clear();
}
