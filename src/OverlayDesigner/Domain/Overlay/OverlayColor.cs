using System.Text.RegularExpressions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// An arbitrary sRGB colour with alpha, authored on an
/// <see cref="OverlayElement"/> (spec 300, #2349, ADR-0165 §2). Canonical
/// form is always eight upper-case hex digits (<c>#RRGGBBAA</c>); a
/// six-digit value is accepted and canonicalised to an opaque <c>FF</c>
/// alpha. The private constructor makes that canonical form an invariant
/// rather than a convention — <see cref="From"/> is the only way in.
///
/// <para>
/// Kind-agnostic by design: any well-formed value is a valid colour. The
/// text-legibility floor is a rule about a <b>text</b> element, so it lives
/// with <see cref="OverlayElement.TextElement"/> and <see cref="TextLegibility"/>,
/// not here (plan.md §"OverlayColor").
/// </para>
/// </summary>
public sealed partial record OverlayColor : StringValueObject
{
    /// <summary>
    /// The nearest allowed colour to white at 217/255 = 85.1% alpha (spec 463,
    /// ADR-0190 D4) — the migration backfill and the
    /// console's create default (ADR-0165 §2). Pinned by the frontend test
    /// (FR-020); do not change without updating
    /// <c>DEFAULT_OVERLAY_COLOR</c> in <c>apps/shared</c>.
    /// </summary>
    public const string DefaultValue = "#9F9F9FD9";

    /// <summary>
    /// Six or eight hex digits after the <c>#</c>. Pinned by the frontend
    /// test (FR-020) as <c>OVERLAY_COLOR_PATTERN</c> — do not change the
    /// literal without updating both.
    /// </summary>
    public const string Pattern = "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$";

    /// <summary>
    /// The brightest relative luminance an element colour may have (spec 463, ADR-0190 D1). Pinned by the
    /// frontend test (FR-020) as <c>OVERLAY_COLOR_MAX_LUMINANCE</c>.
    /// </summary>
    public const double MaximumRelativeLuminance = 0.35;

    public static OverlayColor Default { get; } = From(DefaultValue);

    private OverlayColor(string value) : base(value)
    {
    }

    public static OverlayColor From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Matches(
            ColorPatternRegex(), "must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA.");

        var canonical = Canonicalise(value);
        Ensure.That(canonical).Satisfies(
            hex => RelativeLuminance.OfHex(hex) <= MaximumRelativeLuminance,
            LuminanceRefusal(canonical));
        return new OverlayColor(canonical);
    }

    /// <summary>
    /// The reason <see cref="From"/> refuses a colour over the cap (the cap and the nearest allowed colour),
    /// so a trust boundary can re-name the parameter without parsing the exception text. Expects a
    /// well-formed colour.
    /// </summary>
    public static string LuminanceRefusal(string color)
    {
        var canonical = Canonicalise(color);
        return $"is brighter than the wall allows (relative luminance above {MaximumRelativeLuminance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}); "
            + $"the nearest allowed colour is {NearestAllowed(canonical)}.";
    }

    /// <summary>
    /// Test-only seam (spec 463, owner ruling on #3108): builds a colour from raw hex WITHOUT the
    /// luminance cap, so the pinned legibility characterisation keeps its original vectors.
    /// <c>internal</c> and granted only to the Domain test assembly; <see cref="From"/> is unchanged.
    /// </summary>
    internal static OverlayColor ForTestingOverCap(string raw)
    {
        Ensure.That(raw).IsNotNullOrWhiteSpace().Matches(
            ColorPatternRegex(), "must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA.");
        return new OverlayColor(Canonicalise(raw));
    }

    /// <summary>
    /// The closest colour at or under <see cref="MaximumRelativeLuminance"/> (spec 463 D3): linear RGB is
    /// scaled by cap / luminance, re-encoded and floored to 8 bits, so chromaticity and alpha survive and
    /// the result never rounds back over the cap. A compliant colour is returned unchanged. Takes a string
    /// because the migrator must compute it for a stored value <see cref="From"/> would refuse.
    /// </summary>
    public static string NearestAllowed(string canonicalHex)
    {
        Ensure.That(canonicalHex).IsNotNullOrWhiteSpace().Matches(
            ColorPatternRegex(), "must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA.");
        canonicalHex = Canonicalise(canonicalHex);

        var luminance = RelativeLuminance.OfHex(canonicalHex);
        if (luminance <= MaximumRelativeLuminance)
        {
            return canonicalHex;
        }

        var scale = MaximumRelativeLuminance / luminance;
        var red = ScaledChannel(canonicalHex.Substring(1, 2), scale);
        var green = ScaledChannel(canonicalHex.Substring(3, 2), scale);
        var blue = ScaledChannel(canonicalHex.Substring(5, 2), scale);
        return $"#{red:X2}{green:X2}{blue:X2}{canonicalHex.Substring(7, 2)}";
    }

    private static string Canonicalise(string value)
    {
        var digits = value[1..];
        var eightDigits = digits.Length == 6 ? digits + "FF" : digits;
        return $"#{eightDigits.ToUpperInvariant()}";
    }

    private static int ScaledChannel(string hexPair, double scale)
    {
        var linear = RelativeLuminance.Linearise(Convert.ToInt32(hexPair, 16) / 255.0) * scale;
        var encoded = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
        return (int)Math.Floor(encoded * 255.0);
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ColorPatternRegex();
}
