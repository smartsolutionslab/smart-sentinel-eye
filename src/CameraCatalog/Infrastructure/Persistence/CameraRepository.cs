using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence.Configurations;
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

    public async Task<Option<Camera>> GetWithinFabAsync(
        FabIdentifier fab, CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();

        // The fab is part of the predicate, not a check afterwards: another
        // plant's camera is never materialised, so it cannot be leaked by a
        // caller that forgets to compare (spec 028 FR-004).
        Camera? found = await dbContext.Cameras
            .FirstOrDefaultAsync(
                candidate => candidate.Id == camera && candidate.Fab == fab,
                cancellationToken);

        return found is null ? Option<Camera>.None : Option<Camera>.Some(found);
    }

    public async Task<bool> ExistsByNameAsync(
        FabIdentifier fab, CameraName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();

        // #1434. `candidate.Name == name` looked case-insensitive because
        // CameraName.Equals compares NormalizedValue — but EF translates the
        // predicate to SQL against the stored column, which holds the original
        // casing, so Equals never ran and the comparison was case-sensitive.
        // Matching on the generated normalised column is both correct and the
        // one the unique index covers.
        // Spec 028 FR-006. Retired cameras are excluded because this check
        // exists to pre-empt the unique index, and the index has never counted
        // them — its filter is `status <> 'Decommissioned'`. Without this the
        // two disagree: the insert the index would accept is refused before it
        // is ever attempted, and a name could never be reused however long the
        // hardware had been gone.
        //
        // Research §1 read the index and concluded FR-006 needed no production
        // code. The index was right; this predicate was the other half.
        return await dbContext.Cameras
            .Where(candidate => candidate.Fab == fab && candidate.Status != CameraStatus.Decommissioned)
            .AnyAsync(
                candidate => EF.Property<string>(candidate, CameraConfiguration.NormalizedNameProperty)
                    == name.NormalizedValue,
                cancellationToken);
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
