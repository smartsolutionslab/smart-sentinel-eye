using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Fakes;

public sealed class InMemoryRuleRepository : IRuleRepository
{
    private readonly List<RuleAggregate> _rules = [];

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
        FabIdentifier fab, RuleName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        // Archived names released for re-use (FR-002), scoped to the fab: the
        // same name in another fab is a different rule, not a clash
        // (spec 013). Matching production here matters — a fake that ignored
        // the fab would let every handler test pass while the real lookup
        // returned another fab's rule.
        RuleAggregate? found = _rules.SingleOrDefault(r =>
            r.Fab == fab && r.Name == name && r.State != RuleState.Archived);
        return Task.FromResult(found is null
            ? Option<RuleAggregate>.None
            : Option<RuleAggregate>.Some(found));
    }

    public void Add(RuleAggregate rule)
    {
        Ensure.That(rule).IsNotNull();
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
