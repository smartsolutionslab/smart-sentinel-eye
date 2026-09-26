using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Read port behind <see cref="EventTypeAdmission"/> — the ingest path's
/// test seam (plan.md §6.1). Mirrors <see cref="IFabStorageReadiness"/> /
/// <c>CatalogFabStorageReadiness</c>: a narrow, ingest-shaped read rather
/// than the write-side <c>ISourceModeRepository</c> /
/// <c>IRegisteredEventTypeRepository</c>, which return tracked aggregates
/// built for a single write and would cost a round trip per distinct key in
/// a batch (spec.md FR-006, plan.md §6.1).
/// </summary>
public interface IEventTypeAdmissionSource
{
    /// <summary>
    /// The <c>(fab, source)</c> pairs among <paramref name="fabs"/> that are
    /// declared strict. An absent pair is discovery (spec.md FR-003) and is
    /// never returned here.
    /// </summary>
    Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken);

    /// <summary>
    /// Those of <paramref name="kinds"/> that are <c>Registered</c> (not
    /// <c>Retired</c>) for <paramref name="fab"/> — the same definition
    /// <c>IRegisteredEventTypeRepository.GetRegisteredAsync</c> uses (spec
    /// 143), read as a set rather than one aggregate at a time.
    /// </summary>
    Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken);
}
