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
    /// White at 217/255 = 85.1% alpha — the migration backfill and the
    /// console's create default (ADR-0165 §2). Pinned by the frontend test
    /// (FR-020); do not change without updating
    /// <c>DEFAULT_OVERLAY_COLOR</c> in <c>apps/shared</c>.
    /// </summary>
    public const string DefaultValue = "#FFFFFFD9";

    /// <summary>
    /// Six or eight hex digits after the <c>#</c>. Pinned by the frontend
    /// test (FR-020) as <c>OVERLAY_COLOR_PATTERN</c> — do not change the
    /// literal without updating both.
    /// </summary>
    public const string Pattern = "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$";

    public static OverlayColor Default { get; } = From(DefaultValue);

    private OverlayColor(string value) : base(value)
    {
    }

    public static OverlayColor From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Matches(
            ColorPatternRegex(), "must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA.");

        string digits = value[1..];
        string eightDigits = digits.Length == 6 ? digits + "FF" : digits;
        return new OverlayColor($"#{eightDigits.ToUpperInvariant()}");
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ColorPatternRegex();
}
