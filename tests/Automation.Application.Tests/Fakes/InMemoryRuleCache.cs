using System.Collections.Concurrent;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Fakes;

/// <summary>
/// Test-side cache that mirrors the production
/// <c>Automation.Infrastructure.Cache.InMemoryRuleCache</c> but
/// without DI / hosted-service plumbing. Stores rules by trigger
/// key and exposes them in <c>CreatedAt</c> ascending order so the
/// last-write-wins fan-out (FR-012) is deterministic.
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
            bucket.RemoveAll(c => c.Identifier == rule.Id);
            bucket.Add(compiled);
            bucket.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        }
    }

    public void Remove(RuleIdentifier rule)
    {
        lock (_gate)
        {
            foreach (List<CompiledRule> bucket in _byTrigger.Values)
            {
                bucket.RemoveAll(c => c.Identifier == rule);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byTrigger.Values.Sum(b => b.Count);
            }
        }
    }
}
