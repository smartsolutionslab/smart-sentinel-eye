namespace SmartSentinelEye.OverlayDesigner.Application.DTOs;

/// <summary>
/// Read-side projection of an Overlay chain returned by
/// <c>GET /overlays/{overlayIdentifier}</c>. Carries every revision in
/// the chain so the management UI can show full history with one fetch.
///
/// <para>
/// <c>Version</c> is the chain's optimistic-concurrency version
/// (ADR-0113), echoed back via <c>If-Match</c> to mutate. The
/// single-overlay read also returns it as an <c>ETag</c>; it is on the
/// body as well so the list endpoint can hand every row a version
/// without a per-row fetch.
/// </para>
/// </summary>
public sealed record OverlayDto(
    Guid OverlayIdentifier,
    int Version,
    string Name,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    IReadOnlyList<OverlayRevisionDto> Revisions);

/// <summary>
/// Per-revision row inside <see cref="OverlayDto"/>. Spec 150 (#2345): the
/// revision now carries an ordered, non-empty set of 1..8 elements instead
/// of one flattened label, so kiosks rendering an overlay can pick up every
/// element's coordinates + font without a second request. Spec 300 (#2349,
/// ADR-0165) widened each element to carry a kind and colour.
/// </summary>
public sealed record OverlayRevisionDto(
    Guid RevisionIdentifier,
    int RevisionNumber,
    string State,
    IReadOnlyList<OverlayElementDto> Elements,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// A single element on a revision, in ordinal (paint) order (spec 150
/// FR-005; spec 300 #2349, ADR-0165 — renamed from <c>OverlayLabelDto</c>).
/// <c>Text</c>/<c>FontSizePx</c> are present exactly when <c>Kind</c> is
/// <c>"Text"</c>.
/// </summary>
public sealed record OverlayElementDto(
    string Kind,
    string Color,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    string? Text,
    int? FontSizePx);

/// <summary>
/// Single-row projection for the management-web overlay picker on the
/// LayoutEditorDialog (spec 004 US2): one entry per chain that currently
/// has a Published revision.
/// </summary>
public sealed record PublishedOverlayDto(
    Guid OverlayIdentifier,
    string Name,
    int RevisionNumber,
    IReadOnlyList<OverlayElementDto> Elements,
    DateTimeOffset PublishedAt);
