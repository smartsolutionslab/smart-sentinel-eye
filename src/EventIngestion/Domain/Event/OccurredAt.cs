using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Wall-clock moment at which the source observed the event
/// (spec 006 FR-003). Always stored as UTC. Constructor allows up
/// to +5 minutes of future skew (small clock drift on PLC gateways)
/// — anything beyond is rejected at <see cref="Event.Ingest"/>.
/// </summary>
public sealed record OccurredAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>
{
    public static OccurredAt From(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Implicit unwrap to the underlying <see cref="DateTimeOffset"/> so EF Core
    /// can translate range comparisons and ordering on the value-converted
    /// column in the read API (<c>e.OccurredAt &gt; x</c> maps to the
    /// <c>occurred_at</c> column; member access on <c>e.OccurredAt.Value</c>
    /// does not translate).
    /// </summary>
    public static implicit operator DateTimeOffset(OccurredAt occurredAt) => occurredAt.Value;

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
