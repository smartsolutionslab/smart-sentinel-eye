using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Stable, sortable, client-generatable identifier for a <see cref="Wall"/>
/// (ADR-0039 + ADR-0090, spec 258 PD-2).
/// </summary>
public readonly record struct WallIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<WallIdentifier>
{
    public static WallIdentifier New() => new(Guid.CreateVersion7());

    public static WallIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(WallIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(WallIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(WallIdentifier left, WallIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(WallIdentifier left, WallIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(WallIdentifier left, WallIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(WallIdentifier left, WallIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
