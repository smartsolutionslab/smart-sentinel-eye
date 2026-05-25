using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

public sealed class CameraRepository(CameraCatalogDbContext dbContext) : ICameraRepository
{
    public async Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Camera? found = await dbContext.Cameras
            .FirstOrDefaultAsync(candidate => candidate.Id == camera, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Camera>.None : Option<Camera>.Some(found);
    }

    public async Task<bool> ExistsByNameAsync(CameraName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        return await dbContext.Cameras
            .AnyAsync(candidate => candidate.Name == name, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        dbContext.Cameras.Add(camera);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
