using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Internal DTO passed from ingress (MQTT subscriber + HTTP
/// endpoints) into the bounded channel and on to the persistence
/// loop. Carries the already-validated value objects plus a
/// caller-supplied <see cref="EventIdentifier"/> (or
/// <c>EventIdentifier.New()</c> for server-minted ids).
/// </summary>
public sealed record EventEnvelope(
    EventIdentifier Identifier,
    FabIdentifier Fab,
    Source Source,
    DeviceIdentifier Device,
    Kind Kind,
    OccurredAt OccurredAt,
    Payload Payload);
