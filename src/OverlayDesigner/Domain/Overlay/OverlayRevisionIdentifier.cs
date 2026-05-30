using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Per-revision identifier, distinct from the chain's
/// <see cref="OverlayIdentifier"/>.
/// </summary>
public readonly record struct OverlayRevisionIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<OverlayRevisionIdentifier>
{
    public static OverlayRevisionIdentifier New() => new(Guid.CreateVersion7());

    public static OverlayRevisionIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OverlayRevisionIdentifier cannot be empty.", nameof(value))
            : new OverlayRevisionIdentifier(value);

    public static implicit operator Guid(OverlayRevisionIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(OverlayRevisionIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(OverlayRevisionIdentifier left, OverlayRevisionIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(OverlayRevisionIdentifier left, OverlayRevisionIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(OverlayRevisionIdentifier left, OverlayRevisionIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(OverlayRevisionIdentifier left, OverlayRevisionIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
