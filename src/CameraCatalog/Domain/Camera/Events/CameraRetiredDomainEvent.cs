using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera.Events;

/// <summary>
/// In-process domain event raised when a Camera reaches its terminal state
/// (spec 028, #1433). Never crosses the bounded-context boundary; the
/// Application layer translates this to CameraRetiredV1 (Shared.Contracts)
/// before publishing to RabbitMQ (ADR-0040).
/// </summary>
/// <remarks>
/// Carries <paramref name="Name"/> so a subscriber can say which name was
/// released without re-reading the aggregate — and it is a name that may
/// belong to a different camera by the time anyone reads the event, because
/// retiring is precisely what frees it for reuse within the fab.
/// </remarks>
public sealed record CameraRetiredDomainEvent(
    CameraIdentifier Camera,
    FabIdentifier Fab,
    CameraName Name,
    DateTimeOffset RetiredAt,
    OperatorIdentifier RetiredBy) : IDomainEvent;
