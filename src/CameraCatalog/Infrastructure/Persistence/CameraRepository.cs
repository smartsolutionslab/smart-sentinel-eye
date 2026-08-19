using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

public sealed class CameraRepository(
    CameraCatalogDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : ICameraRepository
{
    public async Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Camera? found = await dbContext.Cameras
            .FirstOrDefaultAsync(candidate => candidate.Id == camera, cancellationToken);
        return found is null ? Option<Camera>.None : Option<Camera>.Some(found);
    }

    public async Task<bool> ExistsByNameAsync(
        FabIdentifier fab, CameraName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();

        return await dbContext.Cameras
            .Where(candidate => candidate.Fab == fab)
            .AnyAsync(candidate => candidate.Name == name, cancellationToken);
    }

    public void Add(Camera camera)
    {
        Ensure.That(camera).IsNotNull();
        dbContext.Cameras.Add(camera);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Camera[] tracked = dbContext.ChangeTracker
            .Entries<Camera>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first: the announcement is captured into the outbox, and the
        // commit below writes the rows and the messages in one transaction
        // (spec 021 FR-001). It used to be the other way round, and the gap
        // between the two was where an integration event went missing.
        //
        // Which means a handler now runs before the write is durable, and one
        // that throws fails the write rather than leaving the row behind. Every
        // handler on this path publishes and does nothing else - checked across
        // all twelve (research.md R2), not assumed.
        foreach (Camera camera in tracked)
        {
            IDomainEvent[] events = camera.PendingEvents.ToArray();
            camera.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
