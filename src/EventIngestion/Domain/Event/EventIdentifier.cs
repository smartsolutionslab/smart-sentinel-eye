using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Stable, sortable identifier for an ingested event (ADR-0090).
/// For PLC + inference sources the device supplies a Guid v7 in the
/// MQTT payload; for manual + webhook sources the server mints one
/// (spec 006 FR-002).
/// </summary>
public readonly record struct EventIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<EventIdentifier>
{
    public static EventIdentifier New() => new(Guid.CreateVersion7());

    public static EventIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EventIdentifier cannot be empty.", nameof(value))
            : new EventIdentifier(value);

    public static implicit operator Guid(EventIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(EventIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(EventIdentifier left, EventIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(EventIdentifier left, EventIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(EventIdentifier left, EventIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(EventIdentifier left, EventIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
