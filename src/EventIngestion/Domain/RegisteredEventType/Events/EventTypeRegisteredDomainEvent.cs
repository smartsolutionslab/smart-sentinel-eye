using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Events;

/// <summary>
/// Raised by <see cref="RegisteredEventType.Register"/>. Unconsumed on
/// purpose (spec 143 FR-012); its own domain tests are what exercise it.
/// </summary>
public sealed record EventTypeRegisteredDomainEvent(
    RegisteredEventTypeIdentifier Identifier,
    FabIdentifier Fab,
    Kind Kind,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
