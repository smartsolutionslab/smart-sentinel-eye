using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Local identifier for an audit row (spec 009 FR-004). Guid v7
/// so the value is monotonic at the millisecond resolution and
/// sorts naturally on insert order — important because the
/// TimescaleDB hypertable partitions on <c>occurred_at</c> + uses
/// this id as the tie-breaker in cursor pagination.
/// </summary>
public readonly record struct AuditEventIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static AuditEventIdentifier New() => new(Guid.CreateVersion7());

    public static AuditEventIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("AuditEventIdentifier cannot be empty.", nameof(value))
            : new AuditEventIdentifier(value);

    public override string ToString() => Value.ToString();
}
