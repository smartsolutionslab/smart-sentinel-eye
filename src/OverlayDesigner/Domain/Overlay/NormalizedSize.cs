using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The resolution-independent extent of a <see cref="Label"/> within its camera
/// cell (spec 004 FR-005). Both components are in <c>(0, 1]</c>.
///
/// <para>
/// <b>Zero is refused, and that is the whole reason this is a separate type
/// from <see cref="NormalizedPosition"/>:</b> a label with no area is not a
/// label. The bound is exclusive at the bottom, which <c>InRange</c> cannot
/// express, so the guard is <c>Satisfies</c> — the shape
/// <c>GridDimensions.From</c> already uses for its cell cap.
/// </para>
///
/// <para>
/// The factory parameters are <c>normalizedWidth</c> / <c>normalizedHeight</c>
/// while the properties are <c>Width</c> / <c>Height</c>, for the reason given
/// on <see cref="NormalizedPosition"/>: the guard message reaches the caller as
/// the <c>detail</c> of a <c>400</c> and names the request field.
/// </para>
///
/// <para>
/// <b>The message spells out the offending value</b>, because
/// <c>Satisfies</c> — unlike <c>InRange</c> — appends nothing of its own. Spec
/// 060 FR-007 keeps this text, and research R2 accepts exactly one difference
/// from the message it replaces: the colon <c>Satisfies</c> puts after the
/// parameter name. Dropping the <c>; got …</c> tail leaves a caller who sent
/// <c>1.4</c> told the valid interval but not what they sent.
/// </para>
/// </summary>
public sealed record NormalizedSize(decimal Width, decimal Height) : IValueObject
{
    public static NormalizedSize From(decimal normalizedWidth, decimal normalizedHeight)
    {
        Ensure.That(normalizedWidth).Satisfies(
            value => value is > 0m and <= 1m, $"must be in (0, 1]; got {normalizedWidth}.");
        Ensure.That(normalizedHeight).Satisfies(
            value => value is > 0m and <= 1m, $"must be in (0, 1]; got {normalizedHeight}.");
        return new(normalizedWidth, normalizedHeight);
    }

    public override string ToString() => $"{Width}x{Height}";
}
