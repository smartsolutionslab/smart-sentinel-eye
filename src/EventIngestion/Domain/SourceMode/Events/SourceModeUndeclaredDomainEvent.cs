using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode.Events;

/// <summary>
/// Raised by <see cref="SourceMode.Undeclare"/> (spec 317, #2325, FR-013,
/// US4). Unconsumed on purpose, like its siblings (ADR-0040,
/// <see cref="SourceModeDeclaredDomainEvent"/>). The row is deleted by the
/// handler after this is raised — undeclared means no row (spec 269
/// FR-003) — so this event is the only record of the mode it was declared
/// at.
/// </summary>
public sealed record SourceModeUndeclaredDomainEvent(
    SourceModeIdentifier Identifier,
    FabIdentifier Fab,
    Source Source,
    EventTypeMode Mode,
    DateTimeOffset UndeclaredAt,
    OperatorIdentifier UndeclaredBy) : IDomainEvent;
