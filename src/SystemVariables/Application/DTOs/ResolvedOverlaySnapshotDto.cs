namespace SmartSentinelEye.SystemVariables.Application.DTOs;

/// <summary>
/// Snapshot returned by <c>GET /system-variables/snapshot?overlayIdentifier=X</c>.
/// Carries the resolved label text plus the monotonic version that
/// matches the most-recent <c>ResolvedOverlayTextChanged</c> SignalR
/// frame the kiosk has seen.
/// </summary>
public sealed record ResolvedOverlaySnapshotDto(
    Guid OverlayIdentifier,
    string ResolvedText,
    long Version);
