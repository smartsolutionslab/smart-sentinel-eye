using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;

/// <summary>
/// In-process domain event raised when a Revision transitions to
/// Archived — either explicitly, or implicitly when a newer revision
/// in the same chain is published (the atomic-swap path per FR-003).
/// </summary>
public sealed record OverlayRevisionArchivedDomainEvent(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    DateTimeOffset ArchivedAt,
    OperatorIdentifier ArchivedBy) : IDomainEvent;
