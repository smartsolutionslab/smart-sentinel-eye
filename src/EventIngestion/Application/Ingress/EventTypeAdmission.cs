using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// The one collaborator both ingest insertion points consult (spec.md
/// FR-005) — <c>IngestEventCommandHandler</c> with one envelope,
/// <c>IngestEventBatchCommandHandler</c> with the whole batch. A concrete
/// class, not an interface: <see cref="IEventTypeAdmissionSource"/> is the
/// test seam, and a second one here would be speculative (ADR-0036).
/// </summary>
public sealed class EventTypeAdmission(IEventTypeAdmissionSource source)
{
    /// <summary>
    /// Assesses a batch of envelopes at a bounded number of queries — never
    /// one per event (spec.md FR-006, plan.md §6.1):
    /// 1. An empty input returns <see cref="EventTypeVerdicts.AdmitAll"/>.
    /// 2. One <see cref="IEventTypeAdmissionSource.StrictSourcesAsync"/> call
    ///    for the batch's distinct fabs. An empty result also returns
    ///    <see cref="EventTypeVerdicts.AdmitAll"/> — the default case.
    /// 3. One <see cref="IEventTypeAdmissionSource.RegisteredKindsAsync"/>
    ///    call per strict fab present in the batch, not per envelope.
    /// </summary>
    public async Task<EventTypeVerdicts> AssessAsync(
        IReadOnlyCollection<EventEnvelope> envelopes, CancellationToken cancellationToken)
    {
        if (envelopes.Count == 0)
        {
            return EventTypeVerdicts.AdmitAll;
        }

        FabIdentifier[] fabs = [.. envelopes.Select(envelope => envelope.Fab).Distinct()];
        IReadOnlySet<(FabIdentifier Fab, Source Source)> strictSources =
            await source.StrictSourcesAsync(fabs, cancellationToken);

        if (strictSources.Count == 0)
        {
            return EventTypeVerdicts.AdmitAll;
        }

        Dictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab = [];
        foreach (IGrouping<FabIdentifier, EventEnvelope> group in envelopes
            .Where(envelope => strictSources.Contains((envelope.Fab, envelope.Source)))
            .GroupBy(envelope => envelope.Fab))
        {
            Kind[] kinds = [.. group.Select(envelope => envelope.Kind).Distinct()];
            registeredKindsByFab[group.Key] = await source.RegisteredKindsAsync(group.Key, kinds, cancellationToken);
        }

        return EventTypeVerdicts.For(strictSources, registeredKindsByFab);
    }
}
