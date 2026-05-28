using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Event.Events;

/// <summary>
/// In-process domain event raised when an <see cref="Event"/> is
/// minted. The Application layer translates this into a
/// <c>FabEventIngestedV1</c> on the integration bus.
/// </summary>
public sealed record EventIngestedDomainEvent(
    EventIdentifier Identifier,
    FabIdentifier Fab,
    Source Source,
    DeviceIdentifier Device,
    Kind Kind,
    OccurredAt OccurredAt,
    IngestedAt IngestedAt,
    Payload Payload) : IDomainEvent;
