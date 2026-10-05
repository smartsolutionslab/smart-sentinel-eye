namespace SmartSentinelEye.Shared.Contracts.OverlayDesigner;

/// <summary>
/// Integration event published when an Overlay revision transitions to
/// the Published state. Supersedes <c>OverlayRevisionPublishedV2</c> in a
/// clean V3 cut (spec 300, #2349, ADR-0165) — the revision now carries an
/// ordered set of elements with a kind and colour instead of a flat text
/// label. Versioned per ADR-0073 (<c>V&lt;N&gt;</c> suffix marks the shape
/// change); subscribers consume via Wolverine RabbitMQ with per-module
/// queue isolation (ADR-0088).
///
/// <para>
/// Every consumer is in this repo, so there is no dual-publish window —
/// V2 is deleted in the same commit (the <c>OverlayRevisionPublishedV2</c>
/// precedent, spec 150).
/// </para>
///
/// Primitive types only at the wire boundary — value-object types stay
/// inside their owning context per ADR-0040. <c>Overlay</c> is the chain
/// identifier; <c>RevisionNumber</c> identifies which revision within the
/// chain was published. The full element payload is included so subscribers
/// (kiosks via the LayoutLifecycle SignalR hub) can render without an
/// extra fetch.
/// </summary>
public sealed record OverlayRevisionPublishedV3(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    IReadOnlyList<OverlayElementV3> Elements,
    DateTimeOffset PublishedAt,
    Guid PublishedBy,
    EventMetadata Metadata) : IIntegrationEvent;

/// <summary>
/// A single element on the published revision, in paint order (spec 150
/// FR-005; spec 300 #2349, ADR-0165 — one value shape for every kind).
/// Primitives only at the wire (ADR-0040). <c>Text</c>/<c>FontSizePx</c>
/// are present exactly when <c>Kind</c> is <c>"Text"</c>; a
/// <c>Box</c>/<c>Ellipse</c> carries <c>null</c> for both.
/// </summary>
public sealed record OverlayElementV3(
    string Kind,
    string Color,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    string? Text,
    int? FontSizePx);
