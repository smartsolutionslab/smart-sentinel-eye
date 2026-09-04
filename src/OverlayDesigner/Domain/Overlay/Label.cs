using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Single text label rendered over a camera cell (spec 004 FR-005).
/// Carries text + a normalized <see cref="NormalizedPosition"/> and
/// <see cref="NormalizedSize"/> + font size in pixels. Coordinates are
/// resolution-independent so the kiosk-side composite scales to any viewport.
///
/// <para>
/// Placeholder syntax (``{{name}}``) is accepted verbatim in v1; the
/// text is stored as-typed and rendered literally on the kiosk per
/// FR-013. Variable binding lands in spec 005+.
/// </para>
/// </summary>
public sealed record Label(
    string Text,
    NormalizedPosition Position,
    NormalizedSize Size,
    int FontSizePx) : IValueObject
{
    public const int MaximumTextLength = 256;
    public const int MinimumFontSizePx = 8;
    public const int MaximumFontSizePx = 256;

    /// <summary>
    /// EF's materialization constructor. A constructor parameter can only bind
    /// to a mapped scalar, never to a navigation, so EF refuses the primary
    /// constructor outright — <c>Position</c> and <c>Size</c> are owned
    /// references. It binds the two scalars here and sets the two navigations
    /// afterwards, which is why they are handed nulls it immediately replaces.
    ///
    /// <para>
    /// <c>Tile</c>'s equivalent needs <c>#pragma warning disable S1144</c> and
    /// this does not, because SonarAnalyzer's unused-private-member rule does
    /// not raise on a constructor declared in a <c>record</c> — measured, not
    /// assumed: an identical unused private constructor errors in a class and
    /// is silent in a record in this same project.
    /// </para>
    /// </summary>
    private Label(string text, int fontSizePx)
        : this(text, null!, null!, fontSizePx)
    {
    }

    public static Label From(
        string text,
        NormalizedPosition position,
        NormalizedSize size,
        int fontSizePx)
    {
        Ensure.That(text, nameof(text))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumTextLength);

        Ensure.That(position).IsNotNull();
        Ensure.That(size).IsNotNull();

        Ensure.That(fontSizePx).InRange(MinimumFontSizePx, MaximumFontSizePx);

        return new Label(text.Trim(), position, size, fontSizePx);
    }
}
