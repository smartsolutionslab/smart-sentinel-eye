using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

public sealed class OverlayRepository(
    OverlayDesignerDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IOverlayRepository
{
    public async Task<Option<Overlay>> GetByIdentifierAsync(
        OverlayIdentifier overlay, CancellationToken cancellationToken)
    {
        Overlay? found = await dbContext.Overlays
            .FirstOrDefaultAsync(candidate => candidate.Id == overlay, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Overlay>.None : Option<Overlay>.Some(found);
    }

    public async Task<Option<Overlay>> GetByNameAsync(
        OverlayName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Overlay? found = await dbContext.Overlays
            .Where(candidate => candidate.Name == name)
            .Where(candidate => candidate.Revisions.Any(r => r.State != OverlayRevisionState.Archived))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return found is null ? Option<Overlay>.None : Option<Overlay>.Some(found);
    }

    public void Add(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        dbContext.Overlays.Add(overlay);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Overlay[] tracked = dbContext.ChangeTracker
            .Entries<Overlay>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Overlay overlay in tracked)
        {
            IDomainEvent[] events = overlay.PendingEvents.ToArray();
            overlay.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }
}
