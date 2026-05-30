using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Stable, sortable, client-generatable identifier for a stream
/// (ADR-0039 + ADR-0090). Backed by Guid v7 so the timestamp portion gives
/// us natural index ordering in Postgres.
/// </summary>
public readonly record struct StreamIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<StreamIdentifier>
{
    public static StreamIdentifier New() => new(Guid.CreateVersion7());

    public static StreamIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("StreamIdentifier cannot be empty.", nameof(value))
            : new StreamIdentifier(value);

    public static implicit operator Guid(StreamIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(StreamIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(StreamIdentifier left, StreamIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(StreamIdentifier left, StreamIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(StreamIdentifier left, StreamIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(StreamIdentifier left, StreamIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
