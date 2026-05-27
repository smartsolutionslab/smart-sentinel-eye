namespace SmartSentinelEye.Shared.Contracts.OverlayDesigner;

/// <summary>
/// Integration event published when an Overlay revision transitions to
/// the Published state. Versioned per ADR-0073; subscribers consume via
/// Wolverine RabbitMQ with per-module queue isolation (ADR-0088).
///
/// Primitive types only at the wire boundary — value-object types stay
/// inside their owning context per ADR-0040. <c>Overlay</c> is the
/// chain identifier; <c>RevisionNumber</c> identifies which revision
/// within the chain was published. The full Label payload is included
/// so subscribers (kiosks via the LayoutLifecycle SignalR hub) can
/// render without an extra fetch.
/// </summary>
public sealed record OverlayRevisionPublishedV1(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx,
    DateTimeOffset PublishedAt,
    Guid PublishedBy) : IIntegrationEvent;
