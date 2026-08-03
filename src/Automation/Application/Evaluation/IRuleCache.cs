using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Evaluation;

/// <summary>
/// Live cache of Active rules grouped by
/// <c>(fab, source, kind)</c> (spec 007 NFR-003, narrowed by spec 013). The Infrastructure
/// impl seeds from <c>rules</c> on startup and keeps itself fresh
/// via <see cref="Upsert"/> + <see cref="Remove"/> calls from the
/// Publish / Archive handlers.
/// </summary>
public interface IRuleCache
{
    /// <summary>
    /// Returns rules in <paramref name="fab"/> matching the trigger, in
    /// <c>createdAt</c> ascending order (spec FR-012 — last write wins).
    ///
    /// <para>
    /// The fab is part of the key rather than a filter applied to the result.
    /// Filtering afterwards would make lookup cost grow with the number of
    /// rules in *other* fabs, on a path inside the 200 ms event-to-overlay
    /// budget (spec 013 SC-007).
    /// </para>
    /// </summary>
    IReadOnlyList<CompiledRule> LookupActive(FabIdentifier fab, string triggerSource, string triggerKind);

    void Upsert(Rule rule);

    void Remove(RuleIdentifier rule);

    int Count { get; }
}
