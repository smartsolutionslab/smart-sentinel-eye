namespace SmartSentinelEye.LayoutComposition.Api.Requests;

/// <summary>
/// PATCH /layouts/{id}/revisions/{n} body (spec 010). A multi-tile edit
/// replaces the whole grid + tile set atomically — there is no per-tile
/// or tri-state overlay input. Primitives only at the boundary.
/// </summary>
public sealed record EditDraftRequest(
    GridRequest Grid,
    IReadOnlyList<TileRequest> Tiles);
