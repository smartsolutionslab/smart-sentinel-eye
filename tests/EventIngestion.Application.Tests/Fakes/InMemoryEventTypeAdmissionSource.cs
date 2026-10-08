using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IEventTypeAdmissionSource"/> (ADR-0054, no
/// mocking framework). Counts its own calls so
/// <c>EventTypeAdmissionTests</c> can assert the bounded-query shape spec.md
/// FR-006 requires.
///
/// <para>
/// Models registered/retired state with real <see cref="RegisteredEventType"/>
/// aggregates rather than a bare set of kinds, so this fake and
/// <c>IRegisteredEventTypeRepository.GetRegisteredAsync</c> agree on what
/// "registered" means — a retired entry does not count (spec 269 tasks.md
/// T003c.7, plan.md §6.1).
/// </para>
///
/// <para>
/// T006 (spec 317, #2325) — <c>StrictSourcesAsync</c> is replaced by
/// <c>DeclaredSourceModesAsync</c>, which names every declared pair with its
/// mode (plan.md §6.1): a second method here would cost a query per mode,
/// breaking FR-006's bound.
/// </para>
/// </summary>
public sealed class InMemoryEventTypeAdmissionSource : IEventTypeAdmissionSource
{
    private readonly Dictionary<(FabIdentifier Fab, Source Source), EventTypeMode> declaredModes = [];
    private readonly List<RegisteredEventType> registeredEventTypes = [];

    public int DeclaredSourceModesCalls { get; private set; }

    public int RegisteredKindsCalls { get; private set; }

    public void DeclareStrict(FabIdentifier fab, Source source) =>
        declaredModes[(fab, source)] = EventTypeMode.Strict;

    public void DeclareDiscovery(FabIdentifier fab, Source source) =>
        declaredModes[(fab, source)] = EventTypeMode.Discovery;

    public void Register(RegisteredEventType eventType) => registeredEventTypes.Add(eventType);

    public Task<IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode>> DeclaredSourceModesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        DeclaredSourceModesCalls++;

        IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode> result = declaredModes
            .Where(pair => fabs.Contains(pair.Key.Fab))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return Task.FromResult(result);
    }

    public Task<IReadOnlySet<Kind>> RegisteredKindsAsync(
        FabIdentifier fab, IReadOnlyCollection<Kind> kinds, CancellationToken cancellationToken)
    {
        RegisteredKindsCalls++;

        IReadOnlySet<Kind> result = registeredEventTypes
            .Where(eventType => eventType.Fab == fab
                && kinds.Contains(eventType.Kind)
                && eventType.State == RegistrationState.Registered)
            .Select(eventType => eventType.Kind)
            .ToHashSet();
        return Task.FromResult(result);
    }
}
