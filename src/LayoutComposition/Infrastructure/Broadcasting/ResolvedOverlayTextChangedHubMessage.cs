namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay's resolved text changed" SignalR frames
/// (spec 005 FR-013). <c>Version</c> is a monotonic per-overlay
/// counter so kiosks discard out-of-order frames.
/// </summary>
/// <para>
/// <c>Fab</c> names the plant whose values produced <c>ResolvedText</c>. It
/// already picks the group the frame is sent to, but an overlay is a
/// fab-neutral template (ADR-0115), so a screen whose token holds two fabs
/// joins two groups and legitimately receives both plants' frames. Carrying
/// the fab on the frame is what lets that screen keep only its own wall's
/// (ADR-0145).
/// </para>
public sealed record ResolvedOverlayTextChangedHubMessage(Guid Overlay, string Fab, string ResolvedText, long Version);
