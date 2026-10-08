using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// The outcome of <see cref="EventTypeAdmission.AssessAsync"/> for one batch
/// (plan.md §6.1). Depends only on <c>(fab, source, kind)</c>, so duplicates
/// in a batch get the same answer without a second lookup.
///
/// <para>
/// Three outcomes since spec 317 (#2325): <see cref="Refuses"/> (a declared
/// strict pair, unregistered kind), <see cref="Holds"/> (a declared
/// discovery pair, unregistered kind) and admission (everything else,
/// including every undeclared pair — Q1, option A). A refusal and a hold are
/// distinct outcomes for the same envelope and never both true.
/// </para>
/// </summary>
public sealed class EventTypeVerdicts
{
    private readonly IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode> declaredModes;
    private readonly IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab;

    private EventTypeVerdicts(
        IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode> declaredModes,
        IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab)
    {
        this.declaredModes = declaredModes;
        this.registeredKindsByFab = registeredKindsByFab;
    }

    /// <summary>Every envelope is admitted — the default case (spec.md FR-003, Q1 option A).</summary>
    public static EventTypeVerdicts AdmitAll { get; } =
        new(
            new Dictionary<(FabIdentifier, Source), EventTypeMode>(),
            new Dictionary<FabIdentifier, IReadOnlySet<Kind>>());

    public static EventTypeVerdicts For(
        IReadOnlyDictionary<(FabIdentifier Fab, Source Source), EventTypeMode> declaredModes,
        IReadOnlyDictionary<FabIdentifier, IReadOnlySet<Kind>> registeredKindsByFab) =>
        new(declaredModes, registeredKindsByFab);

    /// <summary>
    /// True iff <paramref name="envelope"/>'s <c>(Fab, Source)</c> is
    /// declared strict and its <c>Kind</c> is not registered for that fab.
    /// </summary>
    public bool Refuses(EventEnvelope envelope) =>
        DeclaredMode(envelope) == EventTypeMode.Strict && IsUnregistered(envelope);

    /// <summary>
    /// True iff <paramref name="envelope"/>'s <c>(Fab, Source)</c> is
    /// declared discovery and its <c>Kind</c> is not registered for that fab
    /// (spec 317, #2325, FR-005). An undeclared pair never holds — Q1, option
    /// A keeps today's open behaviour for it.
    /// </summary>
    public bool Holds(EventEnvelope envelope) =>
        DeclaredMode(envelope) == EventTypeMode.Discovery && IsUnregistered(envelope);

    private EventTypeMode? DeclaredMode(EventEnvelope envelope) =>
        declaredModes.TryGetValue((envelope.Fab, envelope.Source), out EventTypeMode? mode) ? mode : null;

    private bool IsUnregistered(EventEnvelope envelope) =>
        !registeredKindsByFab.TryGetValue(envelope.Fab, out IReadOnlySet<Kind>? registeredKinds)
        || !registeredKinds.Contains(envelope.Kind);
}
