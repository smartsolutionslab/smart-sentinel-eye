namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay's resolved text changed" SignalR frames
/// (spec 005 FR-013). <c>Version</c> is a monotonic per-overlay
/// counter so kiosks discard out-of-order frames.
/// </summary>
/// <para>
/// <c>Fab</c> names the plant whose values produced <c>Texts</c>. It
/// already picks the group the frame is sent to, but an overlay is a
/// fab-neutral template (ADR-0115), so a screen whose token holds two fabs
/// joins two groups and legitimately receives both plants' frames. Carrying
/// the fab on the frame is what lets that screen keep only its own wall's
/// (ADR-0145).
/// </para>
/// <para>
/// <c>Texts</c> is template-keyed, not positional (spec 301, #2720, US2,
/// FR-005) — the kiosk pairs by <c>Template</c>, never by index.
/// </para>
public sealed record ResolvedOverlayTextChangedHubMessage(
    Guid Overlay, string Fab, IReadOnlyList<ResolvedOverlayTextHubEntry> Texts, long Version);

/// <summary>One resolved label, keyed by the raw template it was resolved from.</summary>
public sealed record ResolvedOverlayTextHubEntry(string Template, string Resolved);
