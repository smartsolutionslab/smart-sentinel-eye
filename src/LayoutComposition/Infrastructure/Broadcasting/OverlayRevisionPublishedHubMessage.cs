namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay revision became Published" SignalR frames.
/// Primitive types only — mirrors the V3 integration-event shape so
/// kiosks can render without an extra fetch. Spec 150 (#2345):
/// <c>Elements</c> carries the revision's ordered, non-empty set of 1..8
/// elements instead of one flattened label. Spec 300 (#2349, ADR-0165)
/// widened each element to carry a kind and colour — renamed from
/// <c>Labels</c>.
/// </summary>
public sealed record OverlayRevisionPublishedHubMessage(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    IReadOnlyList<OverlayElementHubEntry> Elements,
    DateTimeOffset PublishedAt);

/// <summary>
/// A single element on the published revision, in ordinal (paint) order
/// (spec 150 FR-005; spec 300 #2349, ADR-0165 — renamed from
/// <c>OverlayLabelHubEntry</c>). Primitives only, mirrors
/// <see cref="SmartSentinelEye.LayoutComposition.Domain.Layout.OverlayLifecycleElement"/>.
/// </summary>
public sealed record OverlayElementHubEntry(
    string Kind,
    string Color,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    string? Text,
    int? FontSizePx);
