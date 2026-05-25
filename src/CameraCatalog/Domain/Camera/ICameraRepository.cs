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

    Task<bool> ExistsByNameAsync(CameraName name, CancellationToken cancellationToken);

    void Add(Camera camera);

    Task SaveAsync(CancellationToken cancellationToken);
}
