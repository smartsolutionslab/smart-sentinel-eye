using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode.Events;

/// <summary>
/// Raised by <see cref="SourceMode.Change"/>. Unconsumed on purpose (spec.md
/// FR-014); see <see cref="SourceModeDeclaredDomainEvent"/>.
/// </summary>
public sealed record SourceModeChangedDomainEvent(
    SourceModeIdentifier Identifier,
    FabIdentifier Fab,
    Source Source,
    EventTypeMode From,
    EventTypeMode To,
    DateTimeOffset ChangedAt,
    OperatorIdentifier ChangedBy) : IDomainEvent;
