using System.Collections.Concurrent;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Tests.Fakes;

/// <summary>
/// Test-side cache that mirrors the production
/// <c>Automation.Infrastructure.Cache.InMemoryRuleCache</c> but
/// without DI / hosted-service plumbing. Stores rules by
/// <c>(fab, source, kind)</c> and exposes them in <c>CreatedAt</c> ascending
/// order so the last-write-wins fan-out (FR-012) is deterministic.
///
/// <para>
/// The fab must be part of the key here exactly as it is in production. A
/// fake that keyed on the trigger alone would return another fab's rules and
/// every evaluator test would still pass — which is the shape of the bug
/// being fixed, reproduced in the thing meant to detect it.
/// </para>
/// </summary>
public sealed class InMemoryRuleCache : IRuleCache
{
    private readonly ConcurrentDictionary<(string Fab, string TriggerSource, string TriggerKind), List<CompiledRule>> _byTrigger = new();
    private readonly object _gate = new();

    public IReadOnlyList<CompiledRule> LookupActive(
        FabIdentifier fab, string triggerSource, string triggerKind)
    {
        Ensure.That(fab).IsNotNull();

        if (!_byTrigger.TryGetValue((fab.Value, triggerSource, triggerKind), out List<CompiledRule>? bucket))
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
        Ensure.That(rule).IsNotNull();
        if (rule.State != RuleState.Active)
        {
            return;
        }

        CompiledRule compiled = CompiledRule.From(rule);
        (string Fab, string TriggerSource, string TriggerKind) key =
            (rule.Fab.Value, rule.TriggerSource, rule.TriggerKind);

        List<CompiledRule> bucket = _byTrigger.GetOrAdd(key, _ => []);
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
