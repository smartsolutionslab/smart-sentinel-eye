using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Globally-unique idempotency key for an inbound <c>*V1</c>
/// integration event (spec 009 FR-006). Surfaced from the
/// emitting context's aggregate identifier or a hash of the
/// event payload, depending on the V1 shape. A unique index on
/// this column absorbs Wolverine at-least-once redeliveries via
/// <c>INSERT ... ON CONFLICT (event_identifier) DO NOTHING</c>.
/// </summary>
public sealed record EventIdentifier(Guid Value) : IValueObject<Guid>
{
    public static EventIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EventIdentifier cannot be empty.", nameof(value))
            : new EventIdentifier(value);

    public sealed override string ToString() => Value.ToString();
}
