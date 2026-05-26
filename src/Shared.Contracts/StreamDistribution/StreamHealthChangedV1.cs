namespace SmartSentinelEye.Shared.Contracts.StreamDistribution;

/// <summary>
/// Integration event published when a stream's health state transitions.
/// Versioned per ADR-0073; subscribers consume via Wolverine RabbitMQ with
/// per-module queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, DateTimeOffset) are used at the wire
/// boundary — value-object types stay inside their owning context per
/// ADR-0040. <c>FromState</c> and <c>ToState</c> carry the canonical
/// string form of <c>StreamState</c> (Provisioning / Healthy / Degraded /
/// Offline). <c>Error</c> is populated on transitions into Degraded or
/// Offline, null otherwise.
/// </summary>
public sealed record StreamHealthChangedV1(
    Guid Camera,
    string FromState,
    string ToState,
    DateTimeOffset ChangedAt,
    string? Error) : IIntegrationEvent;
