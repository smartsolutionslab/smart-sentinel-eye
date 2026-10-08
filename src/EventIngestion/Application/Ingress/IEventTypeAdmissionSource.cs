using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

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
    /// Every <c>(fab, source)</c> pair among <paramref name="fabs"/> that has
    /// a declared mode (strict or discovery), with that mode. An absent pair
    /// is undeclared and never returned here — undeclared always admits
    /// (spec 317, #2325, Q1 option A).
    /// </summary>
    Task<IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode>> DeclaredSourceModesAsync(
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
