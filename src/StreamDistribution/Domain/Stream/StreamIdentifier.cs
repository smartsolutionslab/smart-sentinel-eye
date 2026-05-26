using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Stable, sortable, client-generatable identifier for a stream
/// (ADR-0039 + ADR-0090). Backed by Guid v7 so the timestamp portion gives
/// us natural index ordering in Postgres.
/// </summary>
public readonly record struct StreamIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static StreamIdentifier New() => new(Guid.CreateVersion7());

    public static StreamIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("StreamIdentifier cannot be empty.", nameof(value))
            : new StreamIdentifier(value);

    public override string ToString() => Value.ToString();
}
