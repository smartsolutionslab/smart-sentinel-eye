namespace SmartSentinelEye.Shared.Contracts.CameraCatalog;

/// <summary>
/// Integration event published when an operator reads a camera's full stored address through
/// <c>GET /cameras/{camera}/address</c> (ADR-0179). Versioned per ADR-0073.
///
/// <para>
/// <b>It carries no address, on purpose.</b> The event exists because the address was disclosed; carrying
/// it would copy the credential into RabbitMQ, the outbox, dead letters and the audit table. Who read which
/// camera, in which fab, and when, is the whole record.
/// </para>
/// </summary>
public sealed record CameraAddressReadV1(
    Guid Camera,
    string Fab,
    Guid ReadBy,
    DateTimeOffset ReadAt,
    EventMetadata Metadata) : IIntegrationEvent;
