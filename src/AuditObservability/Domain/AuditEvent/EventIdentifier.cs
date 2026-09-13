using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Globally-unique idempotency key for an inbound <c>*V1</c>
/// integration event (spec 009 FR-006). Surfaced from the
/// emitting context's aggregate identifier or a hash of the
/// event payload, depending on the V1 shape. A unique index on
/// this column paired with <c>OccurredAt</c> absorbs Wolverine
/// at-least-once redeliveries via <c>INSERT ... ON CONFLICT
/// (event_identifier, occurred_at) DO NOTHING</c> — the pair, not
/// this column alone, because TimescaleDB forbids a unique index
/// that omits the hypertable partitioning column (TS103); a given
/// event's <c>occurred_at</c> is stable, so the pair still dedups.
/// </summary>
public sealed record EventIdentifier(Guid Value) : IValueObject<Guid>
{
    public static EventIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public sealed override string ToString() => Value.ToString();
}
