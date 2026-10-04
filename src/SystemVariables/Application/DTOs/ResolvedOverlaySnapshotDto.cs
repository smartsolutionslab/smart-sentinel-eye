namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// Snapshot returned by <c>GET /system-variables/-/snapshot?overlayIdentifier=X</c>.
/// Carries every label's resolved text, in ordinal order (spec 150,
/// #2345), plus the monotonic version that matches the most-recent
/// <c>ResolvedOverlayTextChanged</c> SignalR frame the kiosk has seen.
/// </summary>
public sealed record ResolvedOverlaySnapshotDto(
    Guid OverlayIdentifier,
    IReadOnlyList<string> ResolvedTexts,
    long Version);
