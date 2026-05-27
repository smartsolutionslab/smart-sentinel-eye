namespace SmartSentinelEye.OverlayDesigner.Application.DTOs;

/// <summary>
/// Read-side projection of an Overlay chain returned by
/// <c>GET /overlays/{overlayIdentifier}</c>. Carries every revision in
/// the chain so the management UI can show full history with one fetch.
/// </summary>
public sealed record OverlayDto(
    Guid OverlayIdentifier,
    string Name,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    IReadOnlyList<OverlayRevisionDto> Revisions);

/// <summary>
/// Per-revision row inside <see cref="OverlayDto"/>. The label payload
/// is flattened so kiosks rendering an overlay can pick up coordinates
/// + font without a second request.
/// </summary>
public sealed record OverlayRevisionDto(
    Guid RevisionIdentifier,
    int RevisionNumber,
    string State,
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// Single-row projection for the management-web overlay picker on the
/// LayoutEditorDialog (spec 004 US2): one entry per chain that currently
/// has a Published revision.
/// </summary>
public sealed record PublishedOverlayDto(
    Guid OverlayIdentifier,
    string Name,
    int RevisionNumber,
    string Text,
    DateTimeOffset PublishedAt);
