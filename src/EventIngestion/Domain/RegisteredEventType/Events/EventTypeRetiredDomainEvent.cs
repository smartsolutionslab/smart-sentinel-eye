using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Events;

/// <summary>
/// Raised by <see cref="RegisteredEventType.Retire"/>. Unconsumed on purpose
/// (spec 143 FR-012) — see <see cref="EventTypeRegisteredDomainEvent"/>.
/// </summary>
public sealed record EventTypeRetiredDomainEvent(
    RegisteredEventTypeIdentifier Identifier,
    FabIdentifier Fab,
    Kind Kind,
    DateTimeOffset RetiredAt,
    OperatorIdentifier RetiredBy) : IDomainEvent;
