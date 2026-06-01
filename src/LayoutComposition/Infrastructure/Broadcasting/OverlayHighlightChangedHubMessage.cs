namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay should be highlighted" SignalR
/// frames (spec 007 FR-019). The kiosk applies the
/// <c>ssE-overlay-highlight</c> CSS class for
/// <see cref="DurationMs"/> milliseconds, then auto-reverts.
/// </summary>
public sealed record OverlayHighlightChangedHubMessage(Guid Overlay, int DurationMs);
