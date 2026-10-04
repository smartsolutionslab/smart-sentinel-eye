namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published by SystemVariables when a variable change
/// re-resolves an overlay's label texts (spec 005 FR-013, widened to a set
/// by spec 150 / #2345), once per affected overlay. Supersedes
/// <c>ResolvedOverlayTextChangedV1</c> in a clean V2 cut — every consumer is
/// in this repo, so V1 is deleted in the same commit. LayoutComposition
/// subscribes and pushes the <c>ResolvedOverlayTextChanged</c> SignalR
/// frame on the <c>/hubs/layouts</c> hub it owns.
///
/// <para>
/// The resolution itself (reverse index, resolver, the durable
/// per-overlay version counter — issue #2426) stays in SystemVariables —
/// only the already-resolved text travels on the wire, so
/// LayoutComposition needs none of that machinery. <c>Version</c> is a
/// monotonic per-overlay counter the kiosk uses to discard out-of-order
/// frames; it bumps once per change, not once per label.
/// </para>
///
/// <para>
/// <c>ResolvedTexts</c> is a positional list, index-aligned with the
/// revision's labels — it carries no ordinal of its own because the
/// ordinal is dense and both lists come from the same revision, so
/// position already <em>is</em> the ordinal. If #2348 (z-order) makes
/// ordinals sparse, this should become <c>(ordinal, text)</c> pairs
/// instead.
/// </para>
/// </summary>
public sealed record ResolvedOverlayTextChangedV2(
    Guid Overlay,
    IReadOnlyList<string> ResolvedTexts,
    long Version,
    EventMetadata Metadata) : IIntegrationEvent;
