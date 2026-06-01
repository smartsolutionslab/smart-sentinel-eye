using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// 1-indexed monotonic revision number within an Overlay chain.
/// Mirrors LayoutComposition.LayoutRevisionNumber.
/// </summary>
public readonly record struct OverlayRevisionNumber(int Value) : IValueObject<int>
{
    public static readonly OverlayRevisionNumber One = new(1);

    public static OverlayRevisionNumber From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new(value);
    }

    public OverlayRevisionNumber Next() => new(Value + 1);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
