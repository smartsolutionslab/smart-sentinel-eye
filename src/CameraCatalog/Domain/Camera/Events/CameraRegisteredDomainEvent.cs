using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera.Events;

/// <summary>
/// In-process domain event raised when a Camera aggregate finishes
/// registration. Never crosses the bounded-context boundary; the Application
/// layer translates this to CameraRegisteredV1 (Shared.Contracts) before
/// publishing to RabbitMQ (ADR-0040).
/// </summary>
public sealed record CameraRegisteredDomainEvent(
    CameraIdentifier Camera,
    CameraName Name,
    RtspUrl Url,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
