using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Reference to an overlay defined in the OverlayDesigner bounded context
/// (spec 004). Value-copy across contexts per ADR-0040: a rule's action
/// carries the overlay's identifier as a typed wrapper without
/// project-referencing OverlayDesigner.Domain (forbidden by ADR-0027).
///
/// <para>
/// A context-local copy is the established pattern, not a workaround —
/// <c>CameraIdentifier</c> exists separately in LayoutComposition and
/// StreamDistribution for the same reason, and
/// <c>LayoutComposition.Domain.Layout.OverlayIdentifier</c> is this type's
/// twin. §III forbids referencing another context's Domain; it does not
/// require the reference to be a bare <see cref="Guid"/>, which is how this
/// one stayed untyped.
/// </para>
/// </summary>
public readonly record struct OverlayIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<OverlayIdentifier>
{
    public static OverlayIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public static implicit operator Guid(OverlayIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(OverlayIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(OverlayIdentifier left, OverlayIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
