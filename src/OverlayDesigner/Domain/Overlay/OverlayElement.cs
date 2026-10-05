using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Single overlay primitive rendered over a camera cell — a text label, a
/// box, or an ellipse (spec 300, #2349, ADR-0165). One value object
/// discriminated by <see cref="ElementKind"/> rather than a type hierarchy:
/// EF Core owned collections do not map inheritance, and the three kinds
/// share nearly everything (geometry, colour, ordinal, revision lifecycle).
/// Renamed from <c>Label</c> (spec 150) by this spec's behaviour-preserving
/// commit (T004); this commit (T005-T006) adds <see cref="Kind"/>,
/// <see cref="Color"/>, and the three factories below.
///
/// <para>
/// Carries a nullable <see cref="TextContent"/> component, present exactly
/// when <see cref="Kind"/> is <see cref="ElementKind.Text"/> — the presence
/// rule enforced by the three factories, the only public constructors, and
/// backed by a database <c>CHECK</c>. Coordinates are resolution-independent
/// so the kiosk-side composite scales to any viewport (spec 004 FR-005).
/// </para>
///
/// <para>
/// Non-positional, deliberately (phase-6 finding S2): a positional record
/// publishes both its primary constructor and <c>with</c>-expression access
/// to every property, either of which would let a caller build a
/// <c>Box</c> carrying <c>Text</c> without ever reaching
/// <see cref="RequireConsistent"/>. Every property below is <c>private
/// init</c>, so the three factories and the private constructors they and
/// EF's materialisation path use are the only way to set one.
/// </para>
/// </summary>
public sealed record OverlayElement : IValueObject
{
    /// <summary>
    /// The ceiling on a revision's element set (spec 150 ADR-0164; spec 300
    /// ADR-0165 — the cap counts every kind, not only text): a domain
    /// invariant on the render path, not configuration. Raising it is a
    /// future ADR gated on a measured <c>overlay_draw</c> figure on real
    /// kiosk hardware — see <see cref="Overlay.ValidateElements"/>.
    /// </summary>
    public const int MaxElements = 8;

    /// <summary>
    /// This element's zero-based, dense place within its revision's element
    /// set (spec 150 FR-005). A private field mapped by EF as a field-backed
    /// property, exposed to the domain only through <see cref="Ordinal"/> —
    /// never as a bare <c>int</c> property (constitution §II) — mirroring
    /// <c>Tile.Row</c>/<c>Col</c>.
    /// </summary>
    private readonly int ordinal;

    public ElementKind Kind { get; private init; }

    public NormalizedPosition Position { get; private init; } = null!;

    public NormalizedSize Size { get; private init; } = null!;

    public OverlayColor Color { get; private init; } = null!;

    public TextContent? Text { get; private init; }

    public ElementOrdinal Ordinal => ElementOrdinal.From(ordinal);

    /// <summary>
    /// The one constructor that sets every property; every other
    /// constructor below delegates to this one. Private, like the rest —
    /// reached only through the three factories, EF's materialisation
    /// constructor, or <see cref="AtOrdinal"/>.
    /// </summary>
    private OverlayElement(ElementKind kind, NormalizedPosition position, NormalizedSize size, OverlayColor color, TextContent? text)
    {
        Kind = kind;
        Position = position;
        Size = size;
        Color = color;
        Text = text;
    }

    /// <summary>
    /// EF's materialization constructor. A constructor parameter can only
    /// bind to a mapped scalar, never to a navigation, so EF refuses the
    /// five-parameter constructor above outright — <c>Position</c>,
    /// <c>Size</c> and <c>Text</c> are owned references. It binds the two
    /// scalars (<c>Kind</c> and <c>Color</c>, both converted) here and sets
    /// the navigations afterwards (through the <c>private init</c>
    /// accessors, which reflection reaches the same way it reaches a
    /// private setter), which is why they are handed nulls it immediately
    /// replaces.
    /// </summary>
    private OverlayElement(ElementKind kind, OverlayColor color)
        : this(kind, null!, null!, color, null)
    {
    }

    /// <summary>
    /// Copy constructor used by <see cref="AtOrdinal"/>. A plain <c>with</c>
    /// expression cannot reassign <see cref="ordinal"/> — it is
    /// <c>readonly</c>, and a <c>with</c>-produced copy is not itself a
    /// constructor of this type — so the reassignment goes through here
    /// instead.
    /// </summary>
    private OverlayElement(OverlayElement source, int ordinal)
        : this(source.Kind, source.Position, source.Size, source.Color, source.Text)
    {
        this.ordinal = ordinal;
    }

    /// <summary>
    /// A text label. Refuses a colour that <see cref="TextLegibility.IsLegible"/>
    /// rejects at write time — legibility is an invariant of what the system
    /// may hold, not a render-time concern (ADR-0165 §2) — naming
    /// <paramref name="color"/> and carrying its minimum alpha in the
    /// message, which the endpoint's existing <c>catch (ArgumentException)</c>
    /// turns into a 400.
    /// </summary>
    public static OverlayElement TextElement(
        string text, int fontSizePx, NormalizedPosition position, NormalizedSize size, OverlayColor color)
    {
        Ensure.That(position).IsNotNull();
        Ensure.That(size).IsNotNull();
        Ensure.That(color).IsNotNull();

        if (!TextLegibility.IsLegible(color))
        {
            byte minimumAlpha = TextLegibility.MinimumAlpha(color);
            throw new ArgumentException(
                $"A text surface coloured {color.Value[..7]} needs alpha of at least {minimumAlpha:X2} to stay readable over any video.",
                nameof(color));
        }

        TextContent content = TextContent.From(text, fontSizePx);
        OverlayElement element = new(ElementKind.Text, position, size, color, content);
        element.RequireConsistent();
        return element;
    }

    /// <summary>A rectangular stroke. Any alpha, including fully transparent — strokes carry no readability floor (ADR-0165 §2).</summary>
    public static OverlayElement Box(NormalizedPosition position, NormalizedSize size, OverlayColor color) =>
        Shape(ElementKind.Box, position, size, color);

    /// <summary>An elliptical stroke. Any alpha, including fully transparent — strokes carry no readability floor (ADR-0165 §2).</summary>
    public static OverlayElement Ellipse(NormalizedPosition position, NormalizedSize size, OverlayColor color) =>
        Shape(ElementKind.Ellipse, position, size, color);

    private static OverlayElement Shape(ElementKind kind, NormalizedPosition position, NormalizedSize size, OverlayColor color)
    {
        Ensure.That(position).IsNotNull();
        Ensure.That(size).IsNotNull();
        Ensure.That(color).IsNotNull();

        OverlayElement element = new(kind, position, size, color, null);
        element.RequireConsistent();
        return element;
    }

    /// <summary>
    /// Returns a copy of this element at <paramref name="value"/>'s place in
    /// its revision's set. Used by <see cref="Revision"/> when it (re)builds
    /// its element list, so every element's ordinal matches its index.
    /// </summary>
    internal OverlayElement AtOrdinal(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new OverlayElement(this, value);
    }

    /// <summary>
    /// Returns a deep copy of this element — <see cref="Kind"/> carried over,
    /// with its own fresh <see cref="Position"/>/<see cref="Size"/>/
    /// <see cref="Color"/> and (if present) <see cref="Text"/> rather than
    /// the same owned instances. <see cref="Revision.CloneWithOrdinals"/>
    /// needs this: each of those is itself an EF-owned entity keyed on the
    /// element, so copying the reference instead of the value makes EF try
    /// to re-key one owned graph onto two principal rows and throw (FR-007).
    /// Internal because it exists for that persistence reason, not as a
    /// domain operation — a <c>with</c> expression would do the same thing
    /// for a caller inside this type, but <see cref="Revision"/> is not one,
    /// now that construction is locked to this type (phase-6 finding S2).
    /// </summary>
    internal OverlayElement DeepClone() =>
        new(Kind, Position with { }, Size with { }, Color with { }, Text is null ? null : Text with { });

    /// <summary>
    /// An internal self-check that a <see cref="TextContent"/> is present
    /// iff <see cref="Kind"/> is <see cref="ElementKind.Text"/>, called by
    /// every factory right before it returns. It does <b>not</b> guard
    /// against a caller bypassing the factories — <c>private init</c>
    /// properties and private constructors (phase-6 finding S2) do that —
    /// nor against EF materialising a row that violates the database
    /// <c>CHECK</c> (ADR-0165 §1): materialisation sets properties directly
    /// through reflection and never calls this method. What remains
    /// reachable, and the reason this stays rather than being deleted, is a
    /// future bug in <see cref="TextElement"/> or <see cref="Shape"/>
    /// themselves — a changed call passing a mismatched kind/text pair would
    /// trip this immediately instead of persisting a row the <c>CHECK</c>
    /// alone would have to catch.
    /// </summary>
    private void RequireConsistent()
    {
        bool isText = Kind == ElementKind.Text;
        bool hasText = Text is not null;
        if (isText != hasText)
        {
            throw new InvalidOperationException(
                $"OverlayElement is inconsistent: Kind={Kind} but Text is {(hasText ? "present" : "absent")}.");
        }
    }
}
