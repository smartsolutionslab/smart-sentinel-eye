using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests.Fakes;

/// <summary>
/// Stands in for <see cref="IEventTypeAdmissionSource"/> so
/// <see cref="PersistenceLoopHostedServiceTests"/> can resolve the ingest
/// handlers' new <c>EventTypeAdmission</c> dependency without a source that
/// declares strict or discovery (spec 269 T002). A separate copy of
/// <c>EventIngestion.Application.Tests.Fakes.AdmitAllEventTypeAdmissionSource</c>
/// rather than a shared one: this test project has no reference to
/// Application.Tests.
///
/// <para>
/// T006/T009 (spec 317, #2325) — <c>StrictSourcesAsync</c> is replaced by
/// <c>DeclaredSourceModesAsync</c>, answered with an empty map (no pair
/// declared, exactly as before).
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
