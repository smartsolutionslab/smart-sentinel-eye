using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Camera repository contract (ADR-0041). Implementation lives in
/// CameraCatalog.Infrastructure; the Domain layer has no persistence
/// dependency.
/// </summary>
public interface ICameraRepository
{
    Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken);

    /// <summary>
    /// The camera, only if it belongs to this fab (spec 028 FR-004). Another
    /// fab's camera comes back as <see cref="Option{T}.None"/> — the same
    /// answer as an identifier that names nothing.
    ///
    /// <para>
    /// A separate read rather than a fab check after
    /// <see cref="GetByIdentifierAsync(CameraIdentifier, CancellationToken)"/>,
    /// so the guarantee is structural: a caller cannot forget the check, and
    /// another plant's camera is never loaded in the first place. A camera's
    /// record carries its RTSP address, so "never loaded" is worth more than
    /// "loaded and then refused" (#1397).
    /// </para>
    /// </summary>
    Task<Option<Camera>> GetWithinFabAsync(
        FabIdentifier fab, CameraIdentifier camera, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the fab already holds a live camera of this name. Takes a fab
    /// because a name is unique only within one (spec 015) — without it the
    /// question is ambiguous the moment two plants use the same name, which is
    /// precisely what this feature allows.
    /// </summary>
    Task<bool> ExistsByNameAsync(FabIdentifier fab, CameraName name, CancellationToken cancellationToken);

    void Add(Camera camera);

    Task SaveAsync(CancellationToken cancellationToken);
}
