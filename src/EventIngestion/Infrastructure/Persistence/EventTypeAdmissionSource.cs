using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// EF-backed <see cref="IEventTypeAdmissionSource"/> (plan.md §7).
///
/// <para>
/// Withheld for phase A (tasks.md T002): both methods return empty sets
/// without querying — no <c>source_modes</c> table exists yet (that is T008),
/// and <see cref="EventTypeAdmission"/> does not call this class until T005
/// either way. Phase C (T007) fills in the two no-tracking queries against
/// <c>EventIngestionDbContext</c>.
/// </para>
/// </summary>
public sealed class EventTypeAdmissionSource : IEventTypeAdmissionSource
{
    public Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        _ = fabs;
        return Task.FromResult<IReadOnlySet<(FabIdentifier Fab, Source Source)>>(
            new HashSet<(FabIdentifier, Source)>());
    }

    public Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken)
    {
        _ = fab;
        _ = kinds;
        return Task.FromResult<IReadOnlySet<Kind>>(new HashSet<Kind>());
    }
}
