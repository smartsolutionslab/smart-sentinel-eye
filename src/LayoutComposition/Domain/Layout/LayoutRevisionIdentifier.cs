using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Per-revision identifier, distinct from the chain's
/// <see cref="LayoutIdentifier"/>. Lets us address a specific revision
/// in the EF row mapping and over the wire.
/// </summary>
public readonly record struct LayoutRevisionIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<LayoutRevisionIdentifier>
{
    public static LayoutRevisionIdentifier New() => new(Guid.CreateVersion7());

    public static LayoutRevisionIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("LayoutRevisionIdentifier cannot be empty.", nameof(value))
            : new LayoutRevisionIdentifier(value);

    public static implicit operator Guid(LayoutRevisionIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(LayoutRevisionIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(LayoutRevisionIdentifier left, LayoutRevisionIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(LayoutRevisionIdentifier left, LayoutRevisionIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(LayoutRevisionIdentifier left, LayoutRevisionIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(LayoutRevisionIdentifier left, LayoutRevisionIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
