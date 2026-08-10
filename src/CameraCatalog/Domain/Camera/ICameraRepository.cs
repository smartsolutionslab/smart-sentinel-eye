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
    /// Whether the fab already holds a live camera of this name. Takes a fab
    /// because a name is unique only within one (spec 015) — without it the
    /// question is ambiguous the moment two plants use the same name, which is
    /// precisely what this feature allows.
    /// </summary>
    Task<bool> ExistsByNameAsync(FabIdentifier fab, CameraName name, CancellationToken cancellationToken);

    void Add(Camera camera);

    Task SaveAsync(CancellationToken cancellationToken);
}
