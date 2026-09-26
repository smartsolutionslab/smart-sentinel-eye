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

        // ux_walls_fab_name_ci is unique on (fab, lower(name)), but WallName's
        // own equality is ordinal — an exact == here would let
        // "line 3 rotation" past this check after "Line 3 rotation" already
        // exists, only for the database to refuse it with a generic
        // RESOURCE_ALREADY_EXISTS instead of the typed WALL_NAME_TAKEN. Name is
        // mapped through HasConversion, so there is no column expression to
        // lower-case server-side; walls per fab are few (PD-5 caps scenes, not
        // walls, but the count is the same order), so comparing case-
        // insensitively over the fab's own walls is the straightforward fix.
        List<Wall> inFab = await dbContext.Walls
            .Where(candidate => candidate.Fab == fab)
            .ToListAsync(cancellationToken);

        Wall? found = inFab.FirstOrDefault(
            candidate => string.Equals(candidate.Name.Value, name.Value, StringComparison.OrdinalIgnoreCase));
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
