using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Evaluation;

/// <summary>
/// Live cache of Active rules grouped by trigger
/// <c>(source, kind)</c> (spec 007 NFR-003). The Infrastructure
/// impl seeds from <c>rules</c> on startup and keeps itself fresh
/// via <see cref="Upsert"/> + <see cref="Remove"/> calls from the
/// Publish / Archive handlers.
/// </summary>
public interface IRuleCache
{
    /// <summary>
    /// Returns rules matching the trigger in <c>createdAt</c>
    /// ascending order (spec FR-012 — last write wins).
    /// </summary>
    IReadOnlyList<CompiledRule> LookupActive(string triggerSource, string triggerKind);

    void Upsert(Rule rule);

    void Remove(RuleIdentifier rule);

    int Count { get; }
}
