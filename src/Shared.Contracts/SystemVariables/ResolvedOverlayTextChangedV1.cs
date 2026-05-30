namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published by SystemVariables when a variable change
/// re-resolves an overlay's label text (spec 005 FR-013), once per
/// affected overlay. LayoutComposition subscribes and pushes the
/// <c>ResolvedOverlayTextChanged</c> SignalR frame on the
/// <c>/hubs/layouts</c> hub it owns.
///
/// <para>
/// The resolution itself (reverse index, resolver, per-overlay version
/// counter) stays in SystemVariables — only the already-resolved text
/// travels on the wire, so LayoutComposition needs none of that
/// machinery. <c>Version</c> is a monotonic per-overlay counter the
/// kiosk uses to discard out-of-order frames.
/// </para>
/// </summary>
public sealed record ResolvedOverlayTextChangedV1(
    Guid Overlay,
    string ResolvedText,
    long Version,
    EventMetadata Metadata) : IIntegrationEvent;
