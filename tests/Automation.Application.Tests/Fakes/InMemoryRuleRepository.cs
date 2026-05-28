using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Fakes;

public sealed class InMemoryRuleRepository : IRuleRepository
{
    private readonly List<RuleAggregate> _rules = new();

    public IReadOnlyList<RuleAggregate> Rules => _rules;

    public Task<Option<RuleAggregate>> GetByIdentifierAsync(
        RuleIdentifier rule, CancellationToken cancellationToken)
    {
        RuleAggregate? found = _rules.SingleOrDefault(r => r.Id == rule);
        return Task.FromResult(found is null
            ? Option<RuleAggregate>.None
            : Option<RuleAggregate>.Some(found));
    }

    public Task<Option<RuleAggregate>> GetByNameAsync(
        RuleName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        // Archived names released for re-use (FR-002).
        RuleAggregate? found = _rules.SingleOrDefault(r =>
            r.Name == name && r.State != RuleState.Archived);
        return Task.FromResult(found is null
            ? Option<RuleAggregate>.None
            : Option<RuleAggregate>.Some(found));
    }

    public void Add(RuleAggregate rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (RuleAggregate rule in _rules)
        {
            rule.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
