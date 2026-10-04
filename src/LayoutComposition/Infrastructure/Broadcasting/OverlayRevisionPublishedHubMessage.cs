namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay revision became Published" SignalR frames.
/// Primitive types only — mirrors the V2 integration-event shape so
/// kiosks can render without an extra fetch. Spec 150 (#2345):
/// <c>Labels</c> carries the revision's ordered, non-empty set of 1..8
/// labels instead of one flattened label.
/// </summary>
public sealed record OverlayRevisionPublishedHubMessage(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    IReadOnlyList<OverlayLabelHubEntry> Labels,
    DateTimeOffset PublishedAt);

/// <summary>
/// A single label on the published revision, in ordinal (paint) order
/// (spec 150 FR-005). Primitives only, mirrors
/// <see cref="SmartSentinelEye.LayoutComposition.Domain.Layout.OverlayLifecycleLabel"/>.
/// </summary>
public sealed record OverlayLabelHubEntry(
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx);
