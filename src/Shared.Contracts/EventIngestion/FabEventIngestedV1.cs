namespace SmartSentinelEye.Shared.Contracts.EventIngestion;

/// <summary>
/// Integration event published when EventIngestion has durably
/// persisted a new event (spec 006). Versioned per ADR-0073;
/// subscribers consume via Wolverine RabbitMQ with per-module
/// queue isolation (ADR-0088).
///
/// Primitive types only at the wire boundary per ADR-0040.
/// <c>Payload</c> carries the canonicalised JSON document as a
/// string (≤ 64 KB). <c>Source</c> is one of
/// <c>"plc" | "inference" | "manual" | "webhook"</c>.
/// </summary>
public sealed record FabEventIngestedV1(
    Guid EventIdentifier,
    string Fab,
    string Source,
    string Device,
    string Kind,
    DateTimeOffset OccurredAt,
    DateTimeOffset IngestedAt,
    string Payload) : IIntegrationEvent;
