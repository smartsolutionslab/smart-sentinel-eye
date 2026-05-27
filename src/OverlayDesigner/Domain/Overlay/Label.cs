using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Single text label rendered over a camera cell (spec 004 FR-005).
/// Carries text + normalized (0..1) position + size + font size in
/// pixels. Coordinates are resolution-independent so the kiosk-side
/// composite scales to any viewport.
///
/// <para>
/// Placeholder syntax (``{{name}}``) is accepted verbatim in v1; the
/// text is stored as-typed and rendered literally on the kiosk per
/// FR-013. Variable binding lands in spec 005+.
/// </para>
/// </summary>
public sealed record Label(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx) : IValueObject
{
    public const int MaximumTextLength = 256;
    public const int MinimumFontSizePx = 8;
    public const int MaximumFontSizePx = 256;

    public static Label From(
        string text,
        decimal normalizedX,
        decimal normalizedY,
        decimal normalizedWidth,
        decimal normalizedHeight,
        int fontSizePx)
    {
        string validatedText = Ensure.That(text, nameof(text))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumTextLength)
            .AndReturn();

        EnsureNormalized(normalizedX, nameof(normalizedX));
        EnsureNormalized(normalizedY, nameof(normalizedY));
        EnsurePositiveNormalized(normalizedWidth, nameof(normalizedWidth));
        EnsurePositiveNormalized(normalizedHeight, nameof(normalizedHeight));

        if (fontSizePx is < MinimumFontSizePx or > MaximumFontSizePx)
        {
            throw new ArgumentException(
                $"fontSizePx must be in [{MinimumFontSizePx}, {MaximumFontSizePx}]; got {fontSizePx}.",
                nameof(fontSizePx));
        }

        return new Label(
            validatedText.Trim(),
            normalizedX,
            normalizedY,
            normalizedWidth,
            normalizedHeight,
            fontSizePx);
    }

    private static void EnsureNormalized(decimal value, string parameter)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentException(
                $"{parameter} must be in [0, 1]; got {value}.", parameter);
        }
    }

    private static void EnsurePositiveNormalized(decimal value, string parameter)
    {
        if (value is <= 0m or > 1m)
        {
            throw new ArgumentException(
                $"{parameter} must be in (0, 1]; got {value}.", parameter);
        }
    }
}
