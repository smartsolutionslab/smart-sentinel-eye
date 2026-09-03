using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The resolution-independent top-left corner of a <see cref="Label"/> within
/// its camera cell (spec 004 FR-005). Both components are in <c>[0, 1]</c>, so
/// the kiosk-side composite scales the label to any viewport.
///
/// <para>
/// A position carries no relationship to the <see cref="NormalizedSize"/> it
/// travels with: a label may describe a rectangle running off the right edge,
/// and the kiosk composite clips it. That is true today and is not made an
/// invariant here.
/// </para>
///
/// <para>
/// <b>The factory parameters are <c>normalizedX</c> / <c>normalizedY</c> while
/// the properties are <c>X</c> / <c>Y</c>, deliberately.</b>
/// <see cref="Ensure"/> names the failing parameter in the message, and
/// <c>OverlayEndpoints</c> copies that message verbatim into the <c>detail</c>
/// of a <c>400</c>. The request field is <c>normalizedX</c>, so the caller is
/// told which of their fields is wrong. Renaming these to match the properties
/// would silently change the API's response body.
/// </para>
/// </summary>
public sealed record NormalizedPosition(decimal X, decimal Y) : IValueObject
{
    public static NormalizedPosition From(decimal normalizedX, decimal normalizedY)
    {
        Ensure.That(normalizedX).InRange(0m, 1m);
        Ensure.That(normalizedY).InRange(0m, 1m);
        return new(normalizedX, normalizedY);
    }

    public override string ToString() => $"({X},{Y})";
}
