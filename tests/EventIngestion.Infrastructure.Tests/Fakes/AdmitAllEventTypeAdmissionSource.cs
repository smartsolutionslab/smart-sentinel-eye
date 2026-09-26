using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests.Fakes;

/// <summary>
/// Stands in for <see cref="IEventTypeAdmissionSource"/> so
/// <see cref="PersistenceLoopHostedServiceTests"/> can resolve the ingest
/// handlers' new <c>EventTypeAdmission</c> dependency without a source that
/// declares strict (spec 269 T002). A separate copy of
/// <c>EventIngestion.Application.Tests.Fakes.AdmitAllEventTypeAdmissionSource</c>
/// rather than a shared one: this test project has no reference to
/// Application.Tests.
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
