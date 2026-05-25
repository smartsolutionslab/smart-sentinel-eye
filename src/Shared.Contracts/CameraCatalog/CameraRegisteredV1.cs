namespace SmartSentinelEye.Shared.Contracts.CameraCatalog;

/// <summary>
/// Integration event published when a camera is registered. Versioned per
/// ADR-0073; subscribers consume via Wolverine RabbitMQ with per-module
/// queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, DateTimeOffset) are used at the wire
/// boundary — value-object types stay inside their owning context per
/// ADR-0040.
/// </summary>
public sealed record CameraRegisteredV1(
    Guid Camera,
    string Name,
    string Url,
    DateTimeOffset RegisteredAt,
    Guid RegisteredBy) : IIntegrationEvent;
