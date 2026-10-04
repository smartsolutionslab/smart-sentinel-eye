using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Parses a single hex or <c>oklch()</c> colour literal and converts OKLCH to
/// 8-bit sRGB (CSS Color 4's OKLab → linear sRGB matrices).
///
/// <para>
/// Spec 268 (issue #2336) plan.md §5.1, T001: lifted, unchanged, out of
/// <c>DesignTokenLayerTests</c> (spec 257), which now calls
/// <see cref="ParseColorLiteral"/> instead of carrying its own copy.
/// <c>InteractionStateTests</c> (spec 268) fact 6 resolves the same var()
/// chains this way, then extends past a bare literal to a
/// <c>color-mix(in oklch, …)</c> of two such literals — new behaviour, added
/// after this lift, not part of it.
/// </para>
/// </summary>
internal static class OklchColorResolver
{
    /// <summary>
    /// Parses a trimmed CSS colour literal — a 6-digit hex or an
    /// <c>oklch(L% C H)</c> triple — into 8-bit sRGB. Throws for anything else,
    /// naming the declaration it could not resolve.
    /// </summary>
    internal static (int R, int G, int B) ParseColorLiteral(string value, string forName)
    {
        Match hex = Regex.Match(value, @"^#(?<hex>[0-9a-fA-F]{6})$");
        if (hex.Success)
        {
            string h = hex.Groups["hex"].Value;
            return (
                Convert.ToInt32(h[..2], 16),
                Convert.ToInt32(h[2..4], 16),
                Convert.ToInt32(h[4..6], 16));
        }

        Match oklch = Regex.Match(value, @"^oklch\(\s*(?<l>[\d.]+)%\s+(?<c>[\d.]+)\s+(?<h>[\d.]+)\s*\)$");
        if (oklch.Success)
        {
            double l = double.Parse(oklch.Groups["l"].Value, CultureInfo.InvariantCulture);
            double c = double.Parse(oklch.Groups["c"].Value, CultureInfo.InvariantCulture);
            double h = double.Parse(oklch.Groups["h"].Value, CultureInfo.InvariantCulture);
            return OklchToSrgb(l, c, h);
        }

        throw new InvalidOperationException(
            $"cannot resolve '{forName}' to a literal colour — its value '{value}' is neither a hex nor an "
            + "oklch() literal. This resolver serves a plain primitive at the end of a var() chain; it does not "
            + "evaluate color-mix() itself.");
    }

    /// <summary>
    /// CSS Color 4's OKLab → linear sRGB matrices, verified against
    /// spec-257 plan.md §2.1's table (green-500/red-500/amber-500/gray-900/
    /// gray-950 all round-trip to their listed hex).
    /// </summary>
    internal static (int R, int G, int B) OklchToSrgb(double lightnessPercent, double chroma, double hueDegrees)
    {
        double l = lightnessPercent / 100.0;
        double hueRadians = hueDegrees * Math.PI / 180.0;
        double a = chroma * Math.Cos(hueRadians);
        double b = chroma * Math.Sin(hueRadians);

        double lPrime = l + (0.3963377774 * a) + (0.2158037573 * b);
        double mPrime = l - (0.1055613458 * a) - (0.0638541728 * b);
        double sPrime = l - (0.0894841775 * a) - (1.2914855480 * b);

        double lCubed = lPrime * lPrime * lPrime;
        double mCubed = mPrime * mPrime * mPrime;
        double sCubed = sPrime * sPrime * sPrime;

        double rLinear = (4.0767416621 * lCubed) - (3.3077115913 * mCubed) + (0.2309699292 * sCubed);
        double gLinear = (-1.2684380046 * lCubed) + (2.6097574011 * mCubed) - (0.3413193965 * sCubed);
        double bLinear = (-0.0041960863 * lCubed) - (0.7034186147 * mCubed) + (1.7076147010 * sCubed);

        return (GammaEncodeTo255(rLinear), GammaEncodeTo255(gLinear), GammaEncodeTo255(bLinear));
    }

    private static int GammaEncodeTo255(double linear)
    {
        double clampedLinear = Math.Clamp(linear, 0.0, 1.0);
        double encoded = clampedLinear <= 0.0031308
            ? 12.92 * clampedLinear
            : (1.055 * Math.Pow(clampedLinear, 1.0 / 2.4)) - 0.055;

        return (int)Math.Round(Math.Clamp(encoded, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The two colour spaces a <c>color-mix()</c> declaration in this scale can
    /// name (spec 299 plan.md §4.1, issue #2695). Anything else is unsupported —
    /// <c>ResolveOklch</c>/<c>TryResolveSrgb</c> report it rather than guess.
    /// </summary>
    internal enum MixSpace
    {
        Oklab,
        Oklch,
    }

    /// <summary>
    /// Mixes two OKLCH triples per CSS Color 4 and returns the result as OKLCH
    /// coordinates (<paramref name="first"/>'s <c>Lightness</c> in percent, as
    /// every <see cref="OklchTriple"/>-shaped caller carries it).
    ///
    /// <para>
    /// <see cref="MixSpace.Oklab"/>: each operand is converted to
    /// <c>(L, C·cos h, C·sin h)</c> — Oklab's own <c>a</c>/<c>b</c> — interpolated
    /// componentwise, then converted back with <c>atan2</c>. An achromatic
    /// operand (<c>C = 0</c>) contributes <c>a = b = 0</c> regardless of its
    /// written hue, so mixing toward <c>--black</c>/<c>--white</c> keeps the
    /// other operand's hue exactly — the property spec 299 FR-002 asserts.
    /// </para>
    ///
    /// <para>
    /// <see cref="MixSpace.Oklch"/>: <c>L</c> and <c>C</c> interpolate linearly;
    /// hue interpolates linearly along the <b>shorter arc</b> and is never
    /// borrowed from the other operand — CSS Color 4 treats a hue as missing
    /// only when it is powerless <i>after conversion</i>, and every
    /// <c>oklch()</c> literal in this file writes one, including
    /// <c>--black</c>/<c>--white</c> (hue <c>0</c>, written). This is the space
    /// the old guards wrongly approximated by taking the higher-chroma
    /// operand's hue; this branch exists so that wrong behaviour can still be
    /// modelled and reported in rendered degrees, not merely refused.
    /// </para>
    /// </summary>
    internal static (double Lightness, double Chroma, double Hue) Mix(
        MixSpace space,
        (double Lightness, double Chroma, double Hue) first,
        double firstFraction,
        (double Lightness, double Chroma, double Hue) second)
    {
        double secondFraction = 1.0 - firstFraction;

        if (space == MixSpace.Oklch)
        {
            double lightness = (firstFraction * first.Lightness) + (secondFraction * second.Lightness);
            double chroma = (firstFraction * first.Chroma) + (secondFraction * second.Chroma);
            double hue = InterpolateHueAlongShorterArc(first.Hue, second.Hue, secondFraction);

            return (lightness, chroma, hue);
        }

        (double firstL, double firstA, double firstB) = ToOklab(first);
        (double secondL, double secondA, double secondB) = ToOklab(second);

        double mixedL = (firstFraction * firstL) + (secondFraction * secondL);
        double mixedA = (firstFraction * firstA) + (secondFraction * secondA);
        double mixedB = (firstFraction * firstB) + (secondFraction * secondB);

        double mixedChroma = Math.Sqrt((mixedA * mixedA) + (mixedB * mixedB));
        double mixedHue = mixedChroma < 1e-9
            ? 0.0
            : NormalizeDegrees(Math.Atan2(mixedB, mixedA) * 180.0 / Math.PI);

        return (mixedL * 100.0, mixedChroma, mixedHue);
    }

    private static (double L, double A, double B) ToOklab((double Lightness, double Chroma, double Hue) triple)
    {
        double l = triple.Lightness / 100.0;
        double hueRadians = triple.Hue * Math.PI / 180.0;

        return (l, triple.Chroma * Math.Cos(hueRadians), triple.Chroma * Math.Sin(hueRadians));
    }

    private static double InterpolateHueAlongShorterArc(double startHue, double endHue, double fractionTowardEnd)
    {
        double delta = NormalizeDegrees(endHue - startHue);
        if (delta > 180.0)
        {
            delta -= 360.0;
        }

        return NormalizeDegrees(startHue + (delta * fractionTowardEnd));
    }

    private static double NormalizeDegrees(double degrees)
    {
        double normalized = degrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }
}
