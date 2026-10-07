namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event published by SystemVariables when a variable change
/// re-resolves an overlay's label texts (spec 005 FR-013, widened to a set
/// by spec 150 / #2345), once per affected overlay. Supersedes
/// <c>ResolvedOverlayTextChangedV2</c> in a clean cut (spec 301, #2720, US2,
/// FR-005) — every consumer is in this repo and pre-production, so V2 is
/// deleted in the same commit rather than kept for a deprecation window
/// (ADR-0112 §3; human sign-off recorded on #2720 — see plan.md "Why no
/// ADR"). LayoutComposition subscribes and pushes the
/// <c>ResolvedOverlayTextChanged</c> SignalR frame on the
/// <c>/hubs/layouts</c> hub it owns.
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
/// <c>Texts</c> is template-keyed, not positional: one
/// <see cref="ResolvedOverlayTextV3"/> pair per <b>distinct</b>
/// <c>Text</c> template of the revision, in ordinal order of first
/// appearance. A consumer pairs by <c>Template</c>, never by index — a
/// reorder is then correct at once, even while this set is stale, and the
/// defect class V2's index-aligned list was exposed to (spec 301 spec.md
/// "The caption that stayed behind") cannot recur. Shapes (<c>Box</c>,
/// <c>Ellipse</c>) contribute nothing; V2's "pad the slot with an empty
/// string" rule is retired with it.
/// </para>
/// </summary>
public sealed record ResolvedOverlayTextChangedV3(
    Guid Overlay,
    IReadOnlyList<ResolvedOverlayTextV3> Texts,
    long Version,
    EventMetadata Metadata) : IIntegrationEvent;

/// <summary>
/// One resolved label, keyed by the raw template it was resolved from
/// (spec 301, #2720, US2, FR-005).
/// </summary>
public sealed record ResolvedOverlayTextV3(string Template, string Resolved);
