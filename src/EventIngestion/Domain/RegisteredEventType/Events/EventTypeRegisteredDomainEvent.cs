using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType.Events;

/// <summary>
/// Raised by <see cref="RegisteredEventType.Register"/>. Unconsumed on
/// purpose (spec 143 FR-012) — the sibling
/// <c>WebhookIntegrationRegisteredDomainEvent</c> in this same context is the
/// precedent for a domain event with no subscriber but its own domain tests.
/// </summary>
public sealed record EventTypeRegisteredDomainEvent(
    RegisteredEventTypeIdentifier Identifier,
    FabIdentifier Fab,
    Kind Kind,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
