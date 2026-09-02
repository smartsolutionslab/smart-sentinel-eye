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
///
/// <para>
/// <b>Deliberately smaller than its twin.</b> LayoutComposition's copy is
/// <c>IComparable</c> with four comparison operators and an implicit unwrap,
/// because that context orders tiles and hands the raw <see cref="Guid"/> to
/// EF. Nothing in Automation orders overlays or compares two of them — a rule
/// points at one — so those members would be surface with no caller, tested
/// only to keep a coverage gate quiet. Copying the twin wholesale is exactly
/// how that happens; ADR-0139 refused `IComparable` on `AggregateVersion` for
/// the same reason. They are one edit away if a caller appears.
/// </para>
/// </summary>
public readonly record struct OverlayIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static OverlayIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
