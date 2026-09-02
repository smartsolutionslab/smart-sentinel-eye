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
public sealed class RuleBuilder
{
    // Defaults to munich, which is also what the spec-013 migration backfills
    // pre-existing rules to — so a test that does not care about fabs reads
    // the same as it did before the field existed.
    private FabIdentifier _fab = FabIdentifier.From("munich");
    private RuleName _name = RuleName.From("high-oee-on-fast-cycle");
    private TriggerSource _triggerSource = TriggerSource.From("plc");
    private TriggerKind _triggerKind = TriggerKind.From("PlcCycleStart");
    private RulePredicate _predicate = RulePredicate.From("$.payload.cycleTime <= 30");
    private RuleAction _action = RuleAction.SetVariableValue.From(
        "oeeLine1", "100 - $.payload.cycleTime * 2");
    private OperatorIdentifier _createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock _clock = new(
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture));

    public RuleBuilder WithFab(string fab) { _fab = FabIdentifier.From(fab); return this; }
    public RuleBuilder WithName(string name) { _name = RuleName.From(name); return this; }
    public RuleBuilder WithTriggerSource(string source) { _triggerSource = TriggerSource.From(source); return this; }
    public RuleBuilder WithTriggerKind(string kind) { _triggerKind = TriggerKind.From(kind); return this; }
    public RuleBuilder WithPredicate(string predicate) { _predicate = RulePredicate.From(predicate); return this; }
    public RuleBuilder WithAction(RuleAction action) { _action = action; return this; }
    public RuleBuilder WithCreatedBy(OperatorIdentifier op) { _createdBy = op; return this; }
    public RuleBuilder WithClock(DateTimeOffset now) { _clock = new FakeClock(now); return this; }

    public RuleAggregate Build() => RuleAggregate.Create(
        _fab, _name, _triggerSource, _triggerKind, _predicate, _action, _createdBy, _clock);

    public FakeClock Clock => _clock;
}
