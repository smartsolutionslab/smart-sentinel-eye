namespace SmartSentinelEye.Shared.Contracts.CameraCatalog;

/// <summary>
/// Integration event published when a camera is retired (spec 028, #1433).
/// Versioned per ADR-0073; subscribers consume via Wolverine RabbitMQ with
/// per-module queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, DateTimeOffset) are used at the wire
/// boundary — value-object types stay inside their owning context per
/// ADR-0040.
/// </summary>
/// <remarks>
/// Retirement is terminal, so there is no counterpart event reinstating a
/// camera. Replacement hardware is registered afresh and may take this
/// camera's name, which is why <paramref name="Name"/> is a record of what
/// was released rather than a key a subscriber can rely on.
/// </remarks>
public sealed record CameraRetiredV1(
    Guid Camera,
    string Fab,
    string Name,
    DateTimeOffset RetiredAt,
    Guid RetiredBy,
    EventMetadata Metadata) : IIntegrationEvent;
