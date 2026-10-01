using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Automation's own context-local reference to a layout defined in the
/// LayoutComposition bounded context (spec 296, ADR-0157 §2). A value-copy
/// across contexts per ADR-0040/§III, distinct from
/// <c>LayoutComposition.Domain.Layout.LayoutIdentifier</c>.
///
/// <para>
/// Shaped like <see cref="OverlayIdentifier"/>: no <c>IComparable</c>,
/// nothing here orders layouts — a rule's <see cref="SceneTarget.Layout"/>
/// just names one.
/// </para>
/// </summary>
public readonly record struct LayoutIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static LayoutIdentifier From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new(value);
    }

    public override string ToString() => Value.ToString();
}
