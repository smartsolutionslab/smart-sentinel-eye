using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Stable, sortable, client-generatable identifier for a system
/// variable (ADR-0039 + ADR-0090). The aggregate is the named variable
/// itself; there's no revision chain in spec 005.
/// </summary>
public readonly record struct VariableIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<VariableIdentifier>
{
    public static VariableIdentifier New() => new(Guid.CreateVersion7());

    public static VariableIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("VariableIdentifier cannot be empty.", nameof(value))
            : new VariableIdentifier(value);

    public static implicit operator Guid(VariableIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(VariableIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(VariableIdentifier left, VariableIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(VariableIdentifier left, VariableIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(VariableIdentifier left, VariableIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(VariableIdentifier left, VariableIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
