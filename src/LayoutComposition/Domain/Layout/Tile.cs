using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// A rectangle of a multi-tile layout (spec 010/258, ADR-0112 §1,
/// ADR-0156 §2): a required <see cref="CameraIdentifier"/>, an optional
/// <see cref="OverlayIdentifier"/> (exposed as <see cref="Option{T}"/>
/// per ADR-0048), at an explicit <see cref="GridPosition"/> origin,
/// claiming a <see cref="TileSpan"/> of cells from that origin. A tile
/// is a value object with no identity of its own; the owning
/// <see cref="Layout"/> aggregate validates the whole tile set against
/// the grid (a tile can't see its grid). A camera and an overlay MAY each
/// be reused across tiles (ADR-0112 §2).
///
/// <para>
/// The grid coordinate and span are stored as four <c>int</c> fields so EF
/// can key <c>layout_revision_tiles</c> on <c>(revision_id, row, col)</c>,
/// and the overlay as a nullable <see cref="OverlayValue"/> so the
/// <c>overlay_id</c> column is nullable (EF cannot own a struct value
/// object, nor make a non-nullable <c>Option&lt;T&gt;</c> column
/// nullable). The scalar fields are <b>private</b>: EF's need for scalar
/// columns is not one of constitution §II's four exemptions, so they are
/// mapped by <c>LayoutConfiguration</c> as field-backed properties and the
/// domain sees only <see cref="Position"/> / <see cref="Span"/>.
/// </para>
/// </summary>
public sealed record Tile
{
    private readonly int row;
    private readonly int col;
    private readonly int rowSpan;
    private readonly int colSpan;

    public CameraIdentifier Camera { get; }

    /// <summary>
    /// The nullable backing for <see cref="Overlay"/>, exposed for the EF
    /// mapping (the <c>overlay_id</c> column). Prefer <see cref="Overlay"/>
    /// in domain/application code.
    /// </summary>
    public OverlayIdentifier? OverlayValue { get; }

    /// <summary>
    /// A 1×1 tile at <paramref name="position"/> (spec 258): delegates to
    /// the 4-argument constructor with <see cref="TileSpan.Single"/>. Kept
    /// for the ~15 existing call sites and as the honest meaning of a
    /// single-cell tile (plan §2.2).
    /// </summary>
    public Tile(CameraIdentifier camera, Option<OverlayIdentifier> overlay, GridPosition position)
        : this(camera, overlay, position, TileSpan.Single)
    {
    }

    public Tile(CameraIdentifier camera, Option<OverlayIdentifier> overlay, GridPosition position, TileSpan span)
    {
        Ensure.That(position).IsNotNull();
        Ensure.That(span).IsNotNull();
        Camera = camera;
        OverlayValue = overlay.Match(value => (OverlayIdentifier?)value, () => null);
        row = position.Row;
        col = position.Col;
        rowSpan = span.Rows;
        colSpan = span.Cols;
    }

    // EF materialization constructor — invoked by EF Core via reflection
    // (binds the scalar columns directly), so it reads as "unused" to the
    // analyzer.
#pragma warning disable S1144 // Unused private types or members should be removed
    private Tile(CameraIdentifier camera, OverlayIdentifier? overlayValue, int row, int col, int rowSpan, int colSpan)
#pragma warning restore S1144
    {
        Camera = camera;
        OverlayValue = overlayValue;
        this.row = row;
        this.col = col;
        this.rowSpan = rowSpan;
        this.colSpan = colSpan;
    }

    /// <summary>The optional overlay bound to this tile (ADR-0048).</summary>
    public Option<OverlayIdentifier> Overlay =>
        OverlayValue.HasValue ? Option<OverlayIdentifier>.Some(OverlayValue.Value) : Option<OverlayIdentifier>.None;

    /// <summary>The tile's grid coordinate.</summary>
    public GridPosition Position => new(row, col);

    /// <summary>The rectangle of cells this tile claims from <see cref="Position"/> (spec 258).</summary>
    public TileSpan Span => new(rowSpan, colSpan);

    /// <summary>
    /// True when this tile's occupied-cell rectangle intersects
    /// <paramref name="other"/>'s (ADR-0156 §2) — the general form of the
    /// old "same <see cref="GridPosition"/>" check, which is now the 1×1
    /// instance of this. Two axis-aligned rectangles overlap exactly when
    /// they overlap on both axes.
    ///
    /// <para>
    /// Written in subtraction form, not <c>row &lt; other.row + other.rowSpan</c>
    /// (code review finding B1): <c>row</c>/<c>col</c> are bounded (by
    /// <see cref="GridPosition.From"/>) but <c>rowSpan</c>/<c>colSpan</c> are
    /// not upper-bounded here — a caller-supplied span up to
    /// <see cref="int.MaxValue"/> would overflow that addition and wrap to a
    /// negative number, silently reporting a real overlap as none.
    /// <c>row - other.row</c> cannot itself overflow: both are non-negative
    /// and individually bounded by <see cref="int.MaxValue"/>, so their
    /// difference always fits in <see cref="int"/>. <see cref="Layout"/>'s
    /// <see cref="Layout.ValidateGrid"/> already rejects an out-of-bounds
    /// span before this ever runs, but this method is <c>public</c> and
    /// callable on its own, so it must not depend on that ordering to be
    /// correct.
    /// </para>
    /// </summary>
    public bool Overlaps(Tile other) =>
        row - other.row < other.rowSpan && other.row - row < rowSpan &&
        col - other.col < other.colSpan && other.col - col < colSpan;
}
