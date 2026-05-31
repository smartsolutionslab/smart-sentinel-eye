using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

public sealed class LayoutRepository(
    LayoutCompositionDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : ILayoutRepository
{
    public async Task<Option<Layout>> GetByIdentifierAsync(
        LayoutIdentifier layout, CancellationToken cancellationToken)
    {
        Layout? found = await dbContext.Layouts
            .FirstOrDefaultAsync(candidate => candidate.Id == layout, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Layout>.None : Option<Layout>.Some(found);
    }

    public async Task<Option<Layout>> GetByNameAsync(
        LayoutName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        // FR-006: ignore archived chains for name-uniqueness. A chain is
        // "archived" when every revision is in Archived state. Implemented
        // here in LINQ; the application-level uniqueness check is the
        // authoritative source of truth (the DB index is permissive).
        Layout? found = await dbContext.Layouts
            .Where(candidate => candidate.Name == name)
            .Where(candidate => candidate.Revisions.Any(revision => revision.State != LayoutRevisionState.Archived))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Layout>.None : Option<Layout>.Some(found);
    }

    public void Add(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        dbContext.Layouts.Add(layout);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Layout[] tracked = dbContext.ChangeTracker
            .Entries<Layout>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Layout layout in tracked)
        {
            IDomainEvent[] events = layout.PendingEvents.ToArray();
            layout.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
