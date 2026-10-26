using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// WCAG relative luminance of an sRGB colour, shared by <see cref="TextLegibility"/> and the
/// <see cref="OverlayColor"/> brightness cap (spec 463, #3108, ADR-0190 D1). Alpha does not enter it.
/// <c>double</c> here is computed, not persisted state, so constitution §II does not bind it (ADR-0140).
/// </summary>
public static class RelativeLuminance
{
    public static double Of(OverlayColor color)
    {
        Ensure.That(color).IsNotNull();

        return OfHex(color.Value);
    }

    internal static double OfHex(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        return Of(r, g, b);
    }

    internal static double Of(double r, double g, double b) =>
        (0.2126 * Linearise(r)) + (0.7152 * Linearise(g)) + (0.0722 * Linearise(b));

    internal static double Linearise(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
