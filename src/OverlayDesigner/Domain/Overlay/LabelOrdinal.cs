using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// A <see cref="Label"/>'s zero-based, dense position within its revision's
/// label set (spec 150 FR-005, ADR-0164). Both the EF key discriminator
/// (<c>(revision_id, ordinal)</c> on <c>overlay_revision_labels</c>) and the
/// paint order — deterministic rather than incidental, the half of #2348
/// (z-order) that cannot wait.
/// </summary>
public readonly record struct LabelOrdinal(int Value) : IValueObject<int>
{
    public static LabelOrdinal From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
