using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The text + font size carried by a <see cref="ElementKind.Text"/>
/// <see cref="OverlayElement"/> (spec 300, #2349, ADR-0165 §1). Carries the
/// guards <c>Label.From</c> enforced before this spec, verbatim: non-blank,
/// trimmed, at most <see cref="MaximumTextLength"/> characters, font size in
/// <see cref="MinimumFontSizePx"/>..<see cref="MaximumFontSizePx"/>.
///
/// <para>
/// A nullable component on <see cref="OverlayElement"/> rather than two
/// nullable scalars: the presence rule becomes one reference check
/// (<c>Text is not null</c> ⇔ <c>Kind == Text</c>), not two that could
/// disagree. EF maps a nullable owned reference, which it does not do for
/// <c>Option&lt;T&gt;</c> (CLAUDE.md, ADR-0141).
/// </para>
/// </summary>
public sealed record TextContent(string Value, int FontSizePx) : IValueObject
{
    public const int MaximumTextLength = 256;
    public const int MinimumFontSizePx = 8;
    public const int MaximumFontSizePx = 256;

    public static TextContent From(string text, int fontSizePx)
    {
        Ensure.That(text, nameof(text))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumTextLength);

        Ensure.That(fontSizePx).InRange(MinimumFontSizePx, MaximumFontSizePx);

        return new TextContent(text.Trim(), fontSizePx);
    }
}
