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
/// </summary>
public sealed record LayoutRevisionDto(
    Guid RevisionIdentifier,
    int RevisionNumber,
    string State,
    Guid CameraIdentifier,
    Guid? OverlayIdentifier,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// Single-row projection for the kiosk picker (FR-016): one entry per
/// chain that currently has a Published revision.
/// </summary>
public sealed record PublishedLayoutDto(
    Guid LayoutIdentifier,
    string Name,
    int RevisionNumber,
    Guid CameraIdentifier,
    Guid? OverlayIdentifier,
    DateTimeOffset PublishedAt);
