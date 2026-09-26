namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// POST /layouts request body (spec 010). Primitive types at the trust
/// boundary; validation happens inside value-object constructors and the
/// aggregate's grid invariants. A single-camera layout is simply a 1×1
/// grid with one tile.
/// </summary>
public sealed record CreateLayoutRequest(
    string Name,
    GridRequest Grid,
    IReadOnlyList<TileRequest> Tiles);

/// <summary>The row × column shape of the grid. Primitives only.</summary>
public sealed record GridRequest(int Rows, int Cols);

/// <summary>
/// One tile of the grid: a required camera, an optional overlay
/// (<c>null</c> when unbound), at zero-indexed <c>(Row, Col)</c>, claiming
/// a <c>RowSpan × ColSpan</c> rectangle from that origin (spec 262,
/// ADR-0156 §2; default <c>1×1</c> means exactly what it meant before this
/// feature).
/// </summary>
public sealed record TileRequest(
    Guid CameraIdentifier,
    Guid? OverlayIdentifier,
    int Row,
    int Col,
    int RowSpan = 1,
    int ColSpan = 1);
