namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The domain's half of the compositor-independent worst-case text
/// legibility rule (spec 300, #2349, ADR-0165 §2). A translucent text
/// surface sits over moving video with no fixed luminance, so the only
/// defensible guarantee is a worst case over every possible video pixel,
/// checked under both sRGB-encoded and linear-light alpha compositing so
/// the guarantee does not depend on a kiosk's GPU path.
///
/// <para>
/// Ink <b>selection</b> (which of black/white paints best) is a
/// presentation concern and stays in the frontend style function
/// (<c>textLegibility.ts</c>). This service only answers whether a colour
/// is legible at all, and — for the 400 a refusal returns — the lowest
/// alpha that would be. <c>double</c> here is computed, not persisted
/// state, so constitution §II does not bind it (ADR-0140).
/// </para>
/// </summary>
public static class TextLegibility
{
    /// <summary>WCAG AA for normal text. Pinned by the frontend test (FR-020).</summary>
    public const double MinimumContrast = 4.5;

    /// <summary>
    /// True when <paramref name="color"/>, at its own alpha, clears
    /// <see cref="MinimumContrast"/> against the worse of a pure-white and a
    /// pure-black video pixel, under either compositing model.
    /// </summary>
    public static bool IsLegible(OverlayColor color)
    {
        (var contrastLight, var contrastDark) = WorstCaseContrasts(color);
        return Math.Max(contrastLight, contrastDark) >= MinimumContrast;
    }

    /// <summary>
    /// The lowest two-digit alpha at this colour's RGB that would be legible.
    /// The worst case rises monotonically with alpha (ADR-0165 §2, checked
    /// over the cube), so a linear scan from 0 finds the floor. Runs only
    /// when building a refusal's error message — at most 256 cheap
    /// evaluations, never on a hot path.
    /// </summary>
    public static byte MinimumAlpha(OverlayColor color)
    {
        var rgb = color.Value.Substring(1, 6);
        for (var alpha = 0; alpha <= 255; alpha++)
        {
            var candidate = OverlayColor.From($"#{rgb}{alpha:X2}");
            if (IsLegible(candidate))
            {
                return (byte)alpha;
            }
        }

        throw new InvalidOperationException(
            $"no legible alpha found for #{rgb}; #{rgb}FF should always be legible.");
    }

    private static (double ContrastLight, double ContrastDark) WorstCaseContrasts(OverlayColor color)
    {
        var hex = color.Value;
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        var a = Convert.ToInt32(hex.Substring(7, 2), 16) / 255.0;

        var ls = RelativeLuminance(r, g, b);

        var lumEncodedOverWhite = RelativeLuminance(a * r + (1 - a), a * g + (1 - a), a * b + (1 - a));
        var lumLinearOverWhite = a * ls + (1 - a);
        var lighter = Math.Max(lumEncodedOverWhite, lumLinearOverWhite);

        var lumEncodedOverBlack = RelativeLuminance(a * r, a * g, a * b);
        var lumLinearOverBlack = a * ls;
        var darker = Math.Min(lumEncodedOverBlack, lumLinearOverBlack);

        var contrastLight = 1.05 / (lighter + 0.05);
        var contrastDark = (darker + 0.05) / 0.05;
        return (contrastLight, contrastDark);
    }

    private static double RelativeLuminance(double r, double g, double b) =>
        (0.2126 * Linearise(r)) + (0.7152 * Linearise(g)) + (0.0722 * Linearise(b));

    private static double Linearise(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
