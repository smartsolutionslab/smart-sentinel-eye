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
    /// The ceiling on a revision's label set (spec 150, ADR-0164): a domain
    /// invariant on the render path, not configuration. Raising it is a
    /// future ADR gated on a measured <c>overlay_draw</c> figure on real
    /// kiosk hardware — see <see cref="Overlay.ValidateLabels"/>.
    /// </summary>
    public const int MaxLabels = 8;

    /// <summary>
    /// This label's zero-based, dense place within its revision's label set
    /// (spec 150 FR-005). A private field mapped by EF as a field-backed
    /// property, exposed to the domain only through <see cref="Ordinal"/> —
    /// never as a bare <c>int</c> property (constitution §II) — mirroring
    /// <c>Tile.Row</c>/<c>Col</c>. <c>PrimitiveBoundaryTests</c> would not
    /// actually catch a bare <c>int</c> here (it exempts a member whose
    /// declaring type implements <see cref="IValueObject"/>, and
    /// <see cref="Label"/> does), but the two owned-collection element types
    /// should not disagree about how their EF key is modelled.
    /// </summary>
    private readonly int ordinal;

    public LabelOrdinal Ordinal => LabelOrdinal.From(ordinal);

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

    /// <summary>
    /// Copy constructor used by <see cref="AtOrdinal"/>. A plain <c>with</c>
    /// expression cannot reassign <see cref="ordinal"/> — it is
    /// <c>readonly</c>, and a <c>with</c>-produced copy is not itself a
    /// constructor of this type — so the reassignment goes through here
    /// instead.
    /// </summary>
    private Label(Label source, int ordinal)
        : this(source.Text, source.Position, source.Size, source.FontSizePx)
    {
        this.ordinal = ordinal;
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

    /// <summary>
    /// Returns a copy of this label at <paramref name="value"/>'s place in its
    /// revision's set. Used by <see cref="Revision"/> when it (re)builds its
    /// label list, so every element's ordinal matches its index.
    /// </summary>
    internal Label AtOrdinal(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new Label(this, value);
    }
}
