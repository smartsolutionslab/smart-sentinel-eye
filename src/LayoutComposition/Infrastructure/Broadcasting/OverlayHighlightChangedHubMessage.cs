namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay should be highlighted" SignalR
/// frames (spec 007 FR-019). The kiosk applies the
/// <c>ssE-overlay-highlight</c> CSS class for
/// <see cref="DurationMs"/> milliseconds, then auto-reverts.
/// </summary>
/// <para>
/// <c>Fab</c> names the plant whose rule asked for the highlight. As with the
/// resolved-text frame, the fab picks the group but the overlay is shared
/// across fabs (ADR-0115), so a screen holding two fabs cannot otherwise tell
/// another plant's highlight from its own (ADR-0145).
/// </para>
public sealed record OverlayHighlightChangedHubMessage(Guid Overlay, string Fab, int DurationMs);
