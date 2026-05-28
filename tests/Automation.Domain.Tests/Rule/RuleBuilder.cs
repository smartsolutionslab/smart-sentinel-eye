using System.Globalization;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule.Fakes;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// Hand-written fluent builder for <see cref="RuleAggregate"/> per
/// ADR-0054. Sensible happy-path defaults so tests override only
/// the fields they care about.
/// </summary>
internal sealed class RuleBuilder
{
    private RuleName _name = RuleName.From("high-oee-on-fast-cycle");
    private string _triggerSource = "plc";
    private string _triggerKind = "PlcCycleStart";
    private RulePredicate _predicate = RulePredicate.From("$.payload.cycleTime <= 30");
    private RuleAction _action = RuleAction.SetVariableValue.From(
        "oeeLine1", "100 - $.payload.cycleTime * 2");
    private OperatorIdentifier _createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture));

    public RuleBuilder WithName(string name) { _name = RuleName.From(name); return this; }
    public RuleBuilder WithTriggerSource(string source) { _triggerSource = source; return this; }
    public RuleBuilder WithTriggerKind(string kind) { _triggerKind = kind; return this; }
    public RuleBuilder WithPredicate(string predicate) { _predicate = RulePredicate.From(predicate); return this; }
    public RuleBuilder WithAction(RuleAction action) { _action = action; return this; }
    public RuleBuilder WithCreatedBy(OperatorIdentifier op) { _createdBy = op; return this; }
    public RuleBuilder WithClock(DateTimeOffset now) { _clock = new FakeClock(now); return this; }

    public RuleAggregate Build() => RuleAggregate.Create(
        _name, _triggerSource, _triggerKind, _predicate, _action, _createdBy, _clock);

    public FakeClock Clock => _clock;
}
