using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Local identifier for an audit row (spec 009 FR-004). Guid v7
/// so the value is monotonic at the millisecond resolution and
/// sorts naturally on insert order — important because the
/// TimescaleDB hypertable partitions on <c>occurred_at</c> + uses
/// this id as the tie-breaker in cursor pagination.
/// </summary>
public readonly record struct AuditEventIdentifier(Guid Value)
    : IStronglyTypedId<Guid>, IComparable<AuditEventIdentifier>
{
    public static AuditEventIdentifier New() => new(Guid.CreateVersion7());

    public static AuditEventIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("AuditEventIdentifier cannot be empty.", nameof(value))
            : new AuditEventIdentifier(value);

    /// <summary>
    /// Implicit unwrap to the underlying <see cref="Guid"/> so EF Core can
    /// translate ordering / comparisons on the value-converted id in the
    /// read API (ordering on <c>a.Id</c> maps to the <c>audit_id</c> column;
    /// member access on <c>a.Id.Value</c> does not translate).
    /// </summary>
    public static implicit operator Guid(AuditEventIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 (insert-order) so in-memory
    /// query sorts match the SQL <c>ORDER BY audit_id</c> tie-break.</summary>
    public int CompareTo(AuditEventIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(AuditEventIdentifier left, AuditEventIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(AuditEventIdentifier left, AuditEventIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(AuditEventIdentifier left, AuditEventIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(AuditEventIdentifier left, AuditEventIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
