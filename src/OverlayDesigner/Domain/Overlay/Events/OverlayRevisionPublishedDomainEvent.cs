using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;

/// <summary>
/// In-process domain event raised when a Revision transitions to
/// Published. Translated to <c>OverlayRevisionPublishedV1</c> on the
/// integration bus and to a SignalR broadcast (via the shared
/// <c>ILayoutLifecycleBroadcaster</c> abstraction from LayoutComposition)
/// by the Application layer (spec 004 FR-010 + FR-011).
/// </summary>
public sealed record OverlayRevisionPublishedDomainEvent(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    OverlayName Name,
    Label Label,
    DateTimeOffset PublishedAt,
    OperatorIdentifier PublishedBy) : IDomainEvent;
