using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Reference to an overlay defined in the OverlayDesigner bounded
/// context (spec 004). Value-copy across contexts per ADR-0040: the
/// Layout aggregate carries the overlay's identifier as a typed
/// wrapper without project-referencing OverlayDesigner.Domain
/// (forbidden by ADR-0027). Mirrors the existing
/// <see cref="CameraIdentifier"/> pattern.
/// </summary>
public readonly record struct OverlayIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<OverlayIdentifier>
{
    public static OverlayIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("OverlayIdentifier cannot be empty.", nameof(value))
            : new OverlayIdentifier(value);

    public static implicit operator Guid(OverlayIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(OverlayIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
