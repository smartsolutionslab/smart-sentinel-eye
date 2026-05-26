using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout.Events;

/// <summary>
/// In-process domain event raised when a Revision transitions to
/// Published. Translated to <c>LayoutRevisionPublishedV1</c> on the
/// integration bus and to a SignalR broadcast by the Application layer
/// (spec 003 FR-013).
/// </summary>
public sealed record LayoutRevisionPublishedDomainEvent(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    LayoutName Name,
    CameraIdentifier Camera,
    DateTimeOffset PublishedAt,
    OperatorIdentifier PublishedBy) : IDomainEvent;
