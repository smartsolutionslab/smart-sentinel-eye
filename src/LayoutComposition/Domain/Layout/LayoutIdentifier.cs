using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Stable, sortable, client-generatable identifier for a logical layout
/// chain (ADR-0039 + ADR-0090). The chain is the aggregate boundary; all
/// revisions of the same logical layout share this identifier.
/// </summary>
public readonly record struct LayoutIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<LayoutIdentifier>
{
    public static LayoutIdentifier New() => new(Guid.CreateVersion7());

    public static LayoutIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("LayoutIdentifier cannot be empty.", nameof(value))
            : new LayoutIdentifier(value);

    public static implicit operator Guid(LayoutIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(LayoutIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(LayoutIdentifier left, LayoutIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(LayoutIdentifier left, LayoutIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(LayoutIdentifier left, LayoutIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(LayoutIdentifier left, LayoutIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
