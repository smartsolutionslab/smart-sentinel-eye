using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode.Events;

/// <summary>
/// Raised by <see cref="SourceMode.Declare"/>. Unconsumed on purpose
/// (spec.md FR-014; precedent spec 143 FR-012 and
/// <c>RegisteredEventType.Events.EventTypeRegisteredDomainEvent</c>).
/// </summary>
public sealed record SourceModeDeclaredDomainEvent(
    SourceModeIdentifier Identifier,
    FabIdentifier Fab,
    Source Source,
    EventTypeMode Mode,
    DateTimeOffset DeclaredAt,
    OperatorIdentifier DeclaredBy) : IDomainEvent;
