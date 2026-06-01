namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay's resolved text changed" SignalR frames
/// (spec 005 FR-013). <c>Version</c> is a monotonic per-overlay
/// counter so kiosks discard out-of-order frames.
/// </summary>
public sealed record ResolvedOverlayTextChangedHubMessage(Guid Overlay, string ResolvedText, long Version);
