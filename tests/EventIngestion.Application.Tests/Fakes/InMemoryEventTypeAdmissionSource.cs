using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

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
/// </summary>
public sealed class InMemoryEventTypeAdmissionSource : IEventTypeAdmissionSource
{
    private readonly HashSet<(FabIdentifier Fab, Source Source)> strictPairs = [];
    private readonly List<RegisteredEventType> registeredEventTypes = [];

    public int StrictSourcesCalls { get; private set; }

    public int RegisteredKindsCalls { get; private set; }

    public void DeclareStrict(FabIdentifier fab, Source source) => strictPairs.Add((fab, source));

    public void Register(RegisteredEventType eventType) => registeredEventTypes.Add(eventType);

    public Task<IReadOnlySet<(FabIdentifier Fab, Source Source)>> StrictSourcesAsync(
        IReadOnlyCollection<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        StrictSourcesCalls++;

        IReadOnlySet<(FabIdentifier Fab, Source Source)> result =
            strictPairs.Where(pair => fabs.Contains(pair.Fab)).ToHashSet();
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
