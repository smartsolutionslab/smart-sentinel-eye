namespace SmartSentinelEye.LayoutComposition.Application.DTOs;

/// <summary>
/// Read-side projection of a Layout chain returned by
/// <c>GET /layouts/{layoutIdentifier}</c>. Carries every revision in
/// the chain so the management UI can show the full history with one
/// fetch.
/// </summary>
public sealed record LayoutDto(
    Guid LayoutIdentifier,
    string Name,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    IReadOnlyList<LayoutRevisionDto> Revisions);

/// <summary>
/// Per-revision row inside <see cref="LayoutDto"/>. The list endpoint
/// returns one row per logical chain — the read model collapses the
/// chain to its "current Published" revision when filtering by state.
/// Spec 010: carries the grid dimensions + tile set (the scalar
/// camera/overlay fields are removed, ADR-0112 §3).
/// </summary>
public sealed record LayoutRevisionDto(
    Guid RevisionIdentifier,
    int RevisionNumber,
    string State,
    int GridRows,
    int GridCols,
    IReadOnlyList<TileDto> Tiles,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// Single-row projection for the kiosk picker (FR-016): one entry per
/// chain that currently has a Published revision. Spec 010: carries the
/// grid + tiles of the current Published revision.
/// </summary>
public sealed record PublishedLayoutDto(
    Guid LayoutIdentifier,
    string Name,
    int RevisionNumber,
    int GridRows,
    int GridCols,
    IReadOnlyList<TileDto> Tiles,
    DateTimeOffset PublishedAt);

/// <summary>
/// One tile of a layout grid on the read side: a required camera, an
/// optional overlay (<c>null</c> when unbound), at zero-indexed
/// <c>(Row, Col)</c>. The TS <c>LayoutTile</c> seam type mirrors this
/// shape exactly (plan T015).
/// </summary>
public sealed record TileDto(
    Guid CameraIdentifier,
    Guid? OverlayIdentifier,
    int Row,
    int Col);
