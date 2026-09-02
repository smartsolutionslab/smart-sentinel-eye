using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// A single cell of a multi-tile layout (spec 010, ADR-0112 §1): a
/// required <see cref="CameraIdentifier"/>, an optional
/// <see cref="OverlayIdentifier"/> (exposed as <see cref="Option{T}"/>
/// per ADR-0048), at an explicit <see cref="GridPosition"/>. A tile is a
/// value object with no identity of its own; the owning
/// <see cref="Layout"/> aggregate validates the whole tile set against
/// the grid (a tile can't see its grid). A camera and an overlay MAY each
/// be reused across tiles (ADR-0112 §2).
///
/// <para>
/// The grid coordinate is stored as two <c>int</c> fields so EF can key
/// <c>layout_revision_tiles</c> on <c>(revision_id, row, col)</c>, and the
/// overlay as a nullable <see cref="OverlayValue"/> so the <c>overlay_id</c>
/// column is nullable (EF cannot own a struct value object, nor make a
/// non-nullable <c>Option&lt;T&gt;</c> column nullable). The coordinate fields
/// are <b>private</b>: EF's need for scalar columns is not one of constitution
/// §II's four exemptions, so the pair is mapped by
/// <c>LayoutConfiguration</c> as field-backed properties and the domain sees
/// only <see cref="Position"/>.
/// </para>
/// </summary>
public sealed record Tile
{
    private readonly int row;
    private readonly int col;

    public CameraIdentifier Camera { get; }

    /// <summary>
    /// The nullable backing for <see cref="Overlay"/>, exposed for the EF
    /// mapping (the <c>overlay_id</c> column). Prefer <see cref="Overlay"/>
    /// in domain/application code.
    /// </summary>
    public OverlayIdentifier? OverlayValue { get; }

    public Tile(CameraIdentifier camera, Option<OverlayIdentifier> overlay, GridPosition position)
    {
        Ensure.That(position).IsNotNull();
        Camera = camera;
        OverlayValue = overlay.Match(value => (OverlayIdentifier?)value, () => null);
        row = position.Row;
        col = position.Col;
    }

    // EF materialization constructor — invoked by EF Core via reflection
    // (binds the scalar columns directly), so it reads as "unused" to the
    // analyzer.
#pragma warning disable S1144 // Unused private types or members should be removed
    private Tile(CameraIdentifier camera, OverlayIdentifier? overlayValue, int row, int col)
#pragma warning restore S1144
    {
        Camera = camera;
        OverlayValue = overlayValue;
        this.row = row;
        this.col = col;
    }

    /// <summary>The optional overlay bound to this tile (ADR-0048).</summary>
    public Option<OverlayIdentifier> Overlay =>
        OverlayValue.HasValue ? Option<OverlayIdentifier>.Some(OverlayValue.Value) : Option<OverlayIdentifier>.None;

    /// <summary>The tile's grid coordinate.</summary>
    public GridPosition Position => new(row, col);
}
