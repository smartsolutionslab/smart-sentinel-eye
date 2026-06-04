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
/// (<c>null</c> when unbound), at zero-indexed <c>(Row, Col)</c>.
/// </summary>
public sealed record TileRequest(
    Guid CameraIdentifier,
    Guid? OverlayIdentifier,
    int Row,
    int Col);
