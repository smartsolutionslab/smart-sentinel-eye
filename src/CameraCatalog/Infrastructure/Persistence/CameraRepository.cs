using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

public sealed class CameraRepository(
    CameraCatalogDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : ICameraRepository
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

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Camera[] tracked = dbContext.ChangeTracker
            .Entries<Camera>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Camera camera in tracked)
        {
            IDomainEvent[] events = camera.PendingEvents.ToArray();
            camera.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
