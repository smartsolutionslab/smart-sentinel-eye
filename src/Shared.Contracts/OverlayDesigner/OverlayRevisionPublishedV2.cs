namespace SmartSentinelEye.Shared.Contracts.OverlayDesigner;

/// <summary>
/// Integration event published when an Overlay revision transitions to
/// the Published state. Supersedes <c>OverlayRevisionPublishedV1</c> in a
/// clean V2 cut (spec 150, #2345, ADR-0164) — the revision now carries an
/// ordered set of 1..8 labels instead of a single flattened one. Versioned
/// per ADR-0073 (<c>V&lt;N&gt;</c> suffix marks the shape change);
/// subscribers consume via Wolverine RabbitMQ with per-module queue
/// isolation (ADR-0088).
///
/// <para>
/// Every consumer is in this repo, so there is no dual-publish window —
/// V1 is deleted in the same commit (the <c>LayoutRevisionPublishedV2</c>
/// precedent, commit <c>a2768788</c>).
/// </para>
///
/// Primitive types only at the wire boundary — value-object types stay
/// inside their owning context per ADR-0040. <c>Overlay</c> is the chain
/// identifier; <c>RevisionNumber</c> identifies which revision within the
/// chain was published. The full label payload is included so subscribers
/// (kiosks via the LayoutLifecycle SignalR hub) can render without an
/// extra fetch.
/// </summary>
public sealed record OverlayRevisionPublishedV2(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    IReadOnlyList<OverlayLabelV2> Labels,
    DateTimeOffset PublishedAt,
    Guid PublishedBy,
    EventMetadata Metadata) : IIntegrationEvent;

/// <summary>
/// A single label on the published revision, in paint order (spec 150
/// FR-005). Primitives only at the wire (ADR-0040).
/// </summary>
public sealed record OverlayLabelV2(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx);
