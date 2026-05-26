using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout.Events;

/// <summary>
/// In-process domain event raised when a Revision transitions to
/// Archived — either explicitly or because a newer revision in the same
/// chain was just published (the atomic-swap path in spec 003 FR-003).
/// </summary>
public sealed record LayoutRevisionArchivedDomainEvent(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    DateTimeOffset ArchivedAt,
    OperatorIdentifier ArchivedBy) : IDomainEvent;
