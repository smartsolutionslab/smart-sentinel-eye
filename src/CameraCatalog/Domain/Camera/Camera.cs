using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Aggregate root for a registered camera. Rich domain model per ADR-0045:
/// state changes happen through behaviour methods that raise domain events;
/// public setters are private.
/// </summary>
public sealed class Camera : AggregateRoot<CameraIdentifier>
{
    public CameraName Name { get; private set; } = null!;

    public RtspUrl Url { get; private set; } = null!;

    public CameraStatus Status { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public OperatorIdentifier RegisteredBy { get; private set; }

    // EF Core / Marten construction.
    private Camera() { }

    public static Camera Register(CameraName name, RtspUrl url, OperatorIdentifier registeredBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(clock);

        Camera camera = new()
        {
            Id = CameraIdentifier.New(),
            Name = name,
            Url = url,
            Status = CameraStatus.Registered,
            RegisteredAt = clock.UtcNow,
            RegisteredBy = registeredBy,
        };

        camera.Raise(new CameraRegisteredDomainEvent(
            Camera: camera.Id,
            Name: name,
            Url: url,
            RegisteredAt: camera.RegisteredAt,
            RegisteredBy: registeredBy));

        return camera;
    }
}
