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
    private FabIdentifier fab = FabIdentifier.From("munich");
    private RuleName name = RuleName.From("high-oee-on-fast-cycle");
    private TriggerSource triggerSource = TriggerSource.From("plc");
    private TriggerKind triggerKind = TriggerKind.From("PlcCycleStart");
    private RulePredicate predicate = RulePredicate.From("$.payload.cycleTime <= 30");
    private RuleAction action = RuleAction.SetVariableValue.From(
        "oeeLine1", "100 - $.payload.cycleTime * 2");
    private OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private FakeClock clock = new(
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture));

    public RuleBuilder WithFab(string fab) { this.fab = FabIdentifier.From(fab); return this; }
    public RuleBuilder WithName(string name) { this.name = RuleName.From(name); return this; }
    public RuleBuilder WithTriggerSource(string source) { triggerSource = TriggerSource.From(source); return this; }
    public RuleBuilder WithTriggerKind(string kind) { triggerKind = TriggerKind.From(kind); return this; }
    public RuleBuilder WithPredicate(string predicate) { this.predicate = RulePredicate.From(predicate); return this; }
    public RuleBuilder WithAction(RuleAction action) { this.action = action; return this; }
    public RuleBuilder WithCreatedBy(OperatorIdentifier op) { createdBy = op; return this; }
    public RuleBuilder WithClock(DateTimeOffset now) { clock = new FakeClock(now); return this; }

    public RuleAggregate Build() => RuleAggregate.Create(
        fab, name, triggerSource, triggerKind, predicate, action, createdBy, clock);

    public FakeClock Clock => clock;
}
