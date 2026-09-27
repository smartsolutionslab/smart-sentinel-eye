using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// EF-backed <see cref="IEventTypeAdmissionSource"/> (plan.md §7). Both
/// queries are no-tracking: this is the ingest-shaped read seam, not the
/// write-side repositories.
/// </summary>
public sealed class EventTypeAdmissionSource(EventIngestionDbContext dbContext) : IEventTypeAdmissionSource
{
    public async Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        List<SourceMode> strict = await dbContext.SourceModes
            .AsNoTracking()
            .Where(sourceMode => fabs.Contains(sourceMode.Fab) && sourceMode.Mode == EventTypeMode.Strict)
            .ToListAsync(cancellationToken);

        return strict.Select(sourceMode => (sourceMode.Fab, sourceMode.Source)).ToHashSet();
    }

    public async Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken)
    {
        List<Kind> registered = await dbContext.RegisteredEventTypes
            .AsNoTracking()
            .Where(eventType => eventType.Fab == fab
                && kinds.Contains(eventType.Kind)
                && eventType.State == RegistrationState.Registered)
            .Select(eventType => eventType.Kind)
            .ToListAsync(cancellationToken);

        return registered.ToHashSet();
    }
}
