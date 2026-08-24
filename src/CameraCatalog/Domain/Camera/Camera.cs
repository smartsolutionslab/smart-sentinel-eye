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
    /// <summary>
    /// The fab this camera belongs to (spec 015). Fixed at registration: a
    /// camera is bolted to a wall in one building, so relocating the device
    /// means registering it afresh rather than moving the record.
    ///
    /// <para>
    /// Load-bearing for access, not only for naming. A camera's record carries
    /// its RTSP address, so reaching another plant's camera is reaching its
    /// video (#1397).
    /// </para>
    /// </summary>
    public FabIdentifier Fab { get; private set; } = null!;

    public CameraName Name { get; private set; } = null!;

    public RtspUrl Url { get; private set; } = null!;

    public CameraStatus Status { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public OperatorIdentifier RegisteredBy { get; private set; }

    // EF Core / Marten construction.
    private Camera() { }

    public static Camera Register(
        FabIdentifier fab, CameraName name, RtspUrl url, OperatorIdentifier registeredBy, IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        Ensure.That(url).IsNotNull();
        Ensure.That(clock).IsNotNull();

        Camera camera = new()
        {
            Id = CameraIdentifier.New(),
            Fab = fab,
            Name = name,
            Url = url,
            Status = CameraStatus.Registered,
            RegisteredAt = clock.UtcNow,
            RegisteredBy = registeredBy,
        };

        camera.Raise(new CameraRegisteredDomainEvent(
            Camera: camera.Id,
            Fab: fab,
            Name: name,
            Url: url,
            RegisteredAt: camera.RegisteredAt,
            RegisteredBy: registeredBy));

        return camera;
    }

    /// <summary>
    /// Records that this camera's hardware is gone (spec 028, #1433).
    /// Terminal: there is no behaviour out of <see cref="CameraStatus.Decommissioned"/>.
    /// Replacement hardware is registered afresh and may take this camera's
    /// name, because retiring releases it within the fab.
    /// </summary>
    /// <remarks>
    /// Idempotent as <b>no event</b>, not merely as no error. A second call
    /// returning quietly while raising again would announce two retirements —
    /// every consumer would see the camera retired twice, and the audit trail
    /// would record it — while the endpoint still answered 204 and looked
    /// correct (FR-005).
    /// </remarks>
    public void Retire(OperatorIdentifier retiredBy, IClock clock)
    {
        Ensure.That(clock).IsNotNull();

        if (Status == CameraStatus.Decommissioned)
        {
            return;
        }

        Status = CameraStatus.Decommissioned;

        Raise(new CameraRetiredDomainEvent(
            Camera: Id,
            Fab: Fab,
            Name: Name,
            RetiredAt: clock.UtcNow,
            RetiredBy: retiredBy));
    }
}
