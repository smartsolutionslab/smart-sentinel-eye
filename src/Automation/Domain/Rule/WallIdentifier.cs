using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Automation's own context-local reference to a wall defined in the
/// LayoutComposition bounded context (spec 296, ADR-0157 §2). A value-copy
/// across contexts per ADR-0040/§III — mirrors <see cref="OverlayIdentifier"/>'s
/// own reasoning for referencing OverlayDesigner without a project reference.
///
/// <para>
/// Shaped like <see cref="OverlayIdentifier"/>, not like
/// <c>LayoutComposition.Domain.Wall.WallIdentifier</c>: no
/// <c>IComparable</c>, no implicit unwrap — nothing here orders walls or
/// hands the raw <see cref="Guid"/> to EF. A rule just points at one.
/// </para>
/// </summary>
public readonly record struct WallIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static WallIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
