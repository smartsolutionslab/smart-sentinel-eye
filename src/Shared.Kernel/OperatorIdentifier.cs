using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Identifies the human operator who performed an action. Cross-context;
/// lives in Shared.Kernel per ADR-0044's shared-kernel exception list.
/// </summary>
public readonly record struct OperatorIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<OperatorIdentifier>
{
    public static OperatorIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OperatorIdentifier cannot be empty.", nameof(value))
            : new OperatorIdentifier(value);

    public static implicit operator Guid(OperatorIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(OperatorIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(OperatorIdentifier left, OperatorIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(OperatorIdentifier left, OperatorIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(OperatorIdentifier left, OperatorIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(OperatorIdentifier left, OperatorIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
