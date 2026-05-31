using System.Collections.Concurrent;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
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
    private readonly ConcurrentDictionary<(string, string), List<CompiledRule>> _byTrigger = new();
    private readonly object _gate = new();

    public IReadOnlyList<CompiledRule> LookupActive(string triggerSource, string triggerKind)
    {
        if (!_byTrigger.TryGetValue((triggerSource, triggerKind), out List<CompiledRule>? bucket))
        {
            return Array.Empty<CompiledRule>();
        }
        lock (_gate)
        {
            return bucket.ToArray();
        }
    }

    public void Upsert(RuleAggregate rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.State != RuleState.Active) return;
        CompiledRule compiled = CompiledRule.From(rule);
        var key = (rule.TriggerSource, rule.TriggerKind);

        List<CompiledRule> bucket = _byTrigger.GetOrAdd(key, _ => new List<CompiledRule>());
        lock (_gate)
        {
            bucket.RemoveAll(compiledRule => compiledRule.Identifier == rule.Id);
            bucket.Add(compiled);
            bucket.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
        }
    }

    public void Remove(RuleIdentifier rule)
    {
        lock (_gate)
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
            lock (_gate)
            {
                return _byTrigger.Values.Sum(bucket => bucket.Count);
            }
        }
    }
}
