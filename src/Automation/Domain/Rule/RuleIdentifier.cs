using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Stable, sortable identifier for an automation rule (ADR-0090).
/// </summary>
public readonly record struct RuleIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<RuleIdentifier>
{
    public static RuleIdentifier New() => new(Guid.CreateVersion7());

    public static RuleIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("RuleIdentifier cannot be empty.", nameof(value))
            : new RuleIdentifier(value);

    public static implicit operator Guid(RuleIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(RuleIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(RuleIdentifier left, RuleIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
