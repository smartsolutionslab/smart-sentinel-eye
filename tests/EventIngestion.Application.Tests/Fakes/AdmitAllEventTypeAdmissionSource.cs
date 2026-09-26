using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Stands in for <see cref="IEventTypeAdmissionSource"/> wherever a test
/// needs an <see cref="EventTypeAdmission"/> but is not exercising strict
/// mode itself — every existing ingest-handler test (spec 269 T002's
/// characterisation baseline) constructs its handler over this rather than
/// <see cref="InMemoryEventTypeAdmissionSource"/>, so a source that never
/// declares strict cannot accidentally start refusing anything.
/// </summary>
public sealed class AdmitAllEventTypeAdmissionSource : IEventTypeAdmissionSource
{
    public Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<(FabIdentifier Fab, Source Source)>>(
            new HashSet<(FabIdentifier, Source)>());

    public Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Kind>>(new HashSet<Kind>());
}
