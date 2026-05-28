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

    public sealed override string ToString() =>
        Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
