using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout.Events;

/// <summary>
/// In-process domain event raised when a Revision transitions to
/// Published. Translated to <c>LayoutRevisionPublishedV2</c> on the
/// integration bus and to a SignalR broadcast by the Application layer
/// (spec 003 FR-013, spec 010). Carries the published revision's full
/// grid + tile set (ADR-0112 §3).
/// </summary>
public sealed record LayoutRevisionPublishedDomainEvent(
    FabIdentifier Fab,
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    LayoutName Name,
    GridDimensions Grid,
    IReadOnlyList<Tile> Tiles,
    DateTimeOffset PublishedAt,
    OperatorIdentifier PublishedBy) : IDomainEvent;
