using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// The outcome of <see cref="EventTypeAdmission.AssessAsync"/> for one batch
/// (plan.md §6.1). Depends only on <c>(fab, source, kind)</c>, so duplicates
/// in a batch get the same answer without a second lookup.
/// </summary>
public sealed class EventTypeVerdicts
{
    private readonly IReadOnlySet<(FabIdentifier Fab, Source Source)> strictSources;
    private readonly IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab;

    private EventTypeVerdicts(
        IReadOnlySet<(FabIdentifier Fab, Source Source)> strictSources,
        IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab)
    {
        this.strictSources = strictSources;
        this.registeredKindsByFab = registeredKindsByFab;
    }

    /// <summary>Every envelope is admitted — the default case (spec.md FR-003).</summary>
    public static EventTypeVerdicts AdmitAll { get; } =
        new(new HashSet<(FabIdentifier, Source)>(), new Dictionary<FabIdentifier, IReadOnlySet<Kind>>());

    public static EventTypeVerdicts For(
        IReadOnlySet<(FabIdentifier Fab, Source Source)> strictSources,
        IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab) =>
        new(strictSources, registeredKindsByFab);

    /// <summary>
    /// True iff <paramref name="envelope"/>'s <c>(Fab, Source)</c> is strict
    /// and its <c>Kind</c> is not registered for that fab.
    /// </summary>
    public bool Refuses(EventEnvelope envelope)
    {
        if (!strictSources.Contains((envelope.Fab, envelope.Source)))
        {
            return false;
        }

        return !registeredKindsByFab.TryGetValue(envelope.Fab, out IReadOnlySet<Kind>? registeredKinds)
            || !registeredKinds.Contains(envelope.Kind);
    }
}
