using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

public sealed class WallRepository(
    LayoutCompositionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IWallRepository
{
    public async Task<Option<Wall>> FindAsync(
        WallIdentifier wall, IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        Ensure.That(fabs).IsNotNull();

        // The fab filter is part of the lookup (US1-14). A wall in another
        // fab therefore comes back as None, exactly like an identifier that
        // matches nothing.
        Wall? found = await dbContext.Walls.FirstOrDefaultAsync(
            candidate => candidate.Id == wall && fabs.Contains(candidate.Fab),
            cancellationToken);
        return found is null ? Option<Wall>.None : Option<Wall>.Some(found);
    }

    public async Task<Option<Wall>> FindByNameAsync(
        FabIdentifier fab, WallName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();

        Wall? found = await dbContext.Walls
            .Where(candidate => candidate.Fab == fab)
            .Where(candidate => candidate.Name == name)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<Wall>.None : Option<Wall>.Some(found);
    }

    public async Task<IReadOnlyList<Wall>> ListAsync(
        IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        Ensure.That(fabs).IsNotNull();

        return await dbContext.Walls
            .Where(candidate => fabs.Contains(candidate.Fab))
            .ToListAsync(cancellationToken);
    }

    public void Add(Wall wall)
    {
        Ensure.That(wall).IsNotNull();

        dbContext.Walls.Add(wall);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Wall[] tracked = dbContext.ChangeTracker
            .Entries<Wall>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first, same reasoning as LayoutRepository.SaveAsync: the
        // announcement is captured into the outbox, and the commit below
        // writes the rows and the messages in one transaction.
        foreach (Wall wall in tracked)
        {
            IDomainEvent[] events = wall.PendingEvents.ToArray();
            wall.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
