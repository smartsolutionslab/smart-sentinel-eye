using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Stands in for <see cref="IEventTypeAdmissionSource"/> wherever a test
/// needs an <see cref="EventTypeAdmission"/> but is not exercising strict or
/// discovery mode itself — every existing ingest-handler test (spec 269
/// T002's characterisation baseline) constructs its handler over this rather
/// than <see cref="InMemoryEventTypeAdmissionSource"/>, so a source that
/// never declares a pair cannot accidentally start refusing or holding
/// anything.
///
/// <para>
/// T006 (spec 317, #2325) — <c>StrictSourcesAsync</c> is replaced by
/// <c>DeclaredSourceModesAsync</c>, which this fake answers with an empty map
/// (no pair declared, exactly as before).
/// </para>
/// </summary>
public sealed class AdmitAllEventTypeAdmissionSource : IEventTypeAdmissionSource
{
    public Task<IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode>> DeclaredSourceModesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode>>(
            new Dictionary<(FabIdentifier, Source), EventTypeMode>());

    public Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Kind>>(new HashSet<Kind>());
}
