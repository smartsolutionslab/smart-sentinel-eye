using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Wire shape of an MQTT payload as published by a PLC gateway or
/// camera. Producers supply <c>eventId</c> (hybrid idempotency,
/// FR-002), <c>kind</c>, <c>occurredAt</c>; the actual application
/// payload sits under <c>payload</c>.
/// </summary>
public sealed record MqttIngressPayload(
    [property: JsonPropertyName("eventId")]    Guid EventId,
    [property: JsonPropertyName("kind")]       string Kind,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("payload")]    JsonElement Payload);
