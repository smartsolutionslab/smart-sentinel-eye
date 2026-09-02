namespace SmartSentinelEye.Shared.Kernel.Primitives;

/// <summary>
/// Marker exposing an aggregate root's optimistic-concurrency version
/// (ADR-0043 as amended by ADR-0113). Non-generic so infrastructure can
/// find aggregate roots in an EF change tracker without knowing each
/// one's identifier type — <see cref="AggregateRoot{TIdentifier}"/> is
/// an open generic and cannot be pattern-matched directly.
/// </summary>
public interface IVersionedAggregate
{
    AggregateVersion Version { get; }
}
