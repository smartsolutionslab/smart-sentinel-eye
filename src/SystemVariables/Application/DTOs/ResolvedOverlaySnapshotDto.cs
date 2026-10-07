namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// Snapshot returned by <c>GET /system-variables/-/snapshot?overlayIdentifier=X</c>.
/// Carries every distinct label template's resolved text, in ordinal order
/// of first appearance (spec 301, #2720, US2, FR-005/FR-006 — was a
/// positional <c>ResolvedTexts</c> list before the template-keyed cut), plus
/// the monotonic version that matches the most-recent
/// <c>ResolvedOverlayTextChanged</c> SignalR frame the kiosk has seen.
/// </summary>
public sealed record ResolvedOverlaySnapshotDto(
    Guid OverlayIdentifier,
    IReadOnlyList<ResolvedOverlayTextDto> Texts,
    long Version);

/// <summary>One resolved label, keyed by the raw template it was resolved from.</summary>
public sealed record ResolvedOverlayTextDto(string Template, string Resolved);
