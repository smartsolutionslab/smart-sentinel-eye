using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

public sealed class LayoutRepository(LayoutCompositionDbContext dbContext, IDomainEventDispatcher domainEventDispatcher)
    : ILayoutRepository
{
    public async Task<Option<Layout>> GetByIdentifierAsync(LayoutIdentifier layout, CancellationToken cancellationToken)
    {
        Layout? found = await dbContext.Layouts.FirstOrDefaultAsync(candidate => candidate.Id == layout, cancellationToken);
        return found is null ? Option<Layout>.None : Option<Layout>.Some(found);
    }

    public async Task<Option<Layout>> GetByNameAsync(
        FabIdentifier fab, LayoutName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        // FR-006: ignore archived chains for name-uniqueness. A chain is
        // "archived" when every revision is in Archived state. Implemented
        // here in LINQ; the application-level uniqueness check is the
        // authoritative source of truth (the DB index is permissive).
        // Fab first: a name is unique only within one (spec 017 FR-019), so
        // without it this returns another fab's layout — and the caller turns
        // that into a 409 that confirms the layout exists.
        Layout? found = await dbContext.Layouts
            .Where(candidate => candidate.Fab == fab)
            .Where(candidate => candidate.Name == name)
            .Where(candidate => candidate.Revisions.Any(revision => revision.State != LayoutRevisionState.Archived))
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<Layout>.None : Option<Layout>.Some(found);
    }

    public void Add(Layout layout)
    {
        Ensure.That(layout).IsNotNull();

        dbContext.Layouts.Add(layout);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Layout[] tracked = dbContext.ChangeTracker
            .Entries<Layout>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (Layout layout in tracked)
        {
            IDomainEvent[] events = layout.PendingEvents.ToArray();
            layout.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }
}
