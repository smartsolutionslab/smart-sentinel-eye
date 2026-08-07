using System.Collections.Concurrent;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Cache;

/// <summary>
/// Production rule cache (spec 007 NFR-003). Process-wide singleton
/// keyed by trigger <c>(source, kind)</c>; values are buckets sorted
/// in <c>createdAt</c> ascending order so the
/// <see cref="RuleEvaluator"/> emits effects in the FR-012
/// last-write-wins order.
///
/// <para>
/// Seeded at startup by <see cref="RuleCacheSeederHostedService"/>
/// (cold start) and kept fresh by direct
/// <see cref="Upsert"/> / <see cref="Remove"/> calls from the
/// Publish / Archive command handlers. For v1 we run one
/// Automation instance per fab; once we scale to multiple
/// instances the seeder will also subscribe to
/// <c>RulePublishedV1</c> / <c>RuleArchivedV1</c> to stay
/// coherent across the cluster.
/// </para>
/// </summary>
public sealed class InMemoryRuleCache : IRuleCache
{
    // Keyed on (fab, source, kind). The fab is part of the key, not a filter
    // applied to the bucket: filtering afterwards would make lookup cost grow
    // with the number of rules in *other* fabs, on a path inside the 200 ms
    // event-to-overlay budget (spec 013 SC-007).
    private readonly ConcurrentDictionary<(string Fab, string TriggerSource, string TriggerKind), List<CompiledRule>> _byTrigger = new();
    private readonly object gate = new();

    public IReadOnlyList<CompiledRule> LookupActive(
        FabIdentifier fab, string triggerSource, string triggerKind)
    {
        Ensure.That(fab).IsNotNull();

        if (!_byTrigger.TryGetValue((fab.Value, triggerSource, triggerKind), out List<CompiledRule>? bucket))
        {
            return Array.Empty<CompiledRule>();
        }
        lock (gate)
        {
            return bucket.ToArray();
        }
    }

    public void Upsert(RuleAggregate rule)
    {
        Ensure.That(rule).IsNotNull();
        if (rule.State != RuleState.Active)
        {
            return;
        }

        CompiledRule compiled = CompiledRule.From(rule);
        (string Fab, string TriggerSource, string TriggerKind) key =
            (rule.Fab.Value, rule.TriggerSource, rule.TriggerKind);

        List<CompiledRule> bucket = _byTrigger.GetOrAdd(key, _ => []);
        lock (gate)
        {
            bucket.RemoveAll(compiledRule => compiledRule.Identifier == rule.Id);
            bucket.Add(compiled);
            bucket.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
        }
    }

    public void Remove(RuleIdentifier rule)
    {
        lock (gate)
        {
            foreach (List<CompiledRule> bucket in _byTrigger.Values)
            {
                bucket.RemoveAll(compiledRule => compiledRule.Identifier == rule);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return _byTrigger.Values.Sum(bucket => bucket.Count);
            }
        }
    }
}
