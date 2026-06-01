namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay revision became Published" SignalR frames.
/// Primitive types only — mirrors the V1 integration-event shape so
/// kiosks can render without an extra fetch.
/// </summary>
public sealed record OverlayRevisionPublishedHubMessage(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx,
    DateTimeOffset PublishedAt);
