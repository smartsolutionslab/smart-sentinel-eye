using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Evaluation;

public class RuleEvaluatorTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private const string PlcCycleStartContext = """
        {
          "source": "plc",
          "kind": "PlcCycleStart",
          "device": "station-4",
          "payload": { "cycleTime": 27 }
        }
        """;

    private static RuleAggregate ActiveRule(
        string name, RuleAction action, DateTimeOffset createdAt,
        string predicate = "$.payload.cycleTime <= 30") =>
        BuildRule(name, action, createdAt, predicate, publish: true);

    private static RuleAggregate BuildRule(
        string name, RuleAction action, DateTimeOffset createdAt,
        string predicate, bool publish)
    {
        RuleAggregate rule = RuleAggregate.Create(
            RuleName.From(name),
            "plc", "PlcCycleStart",
            RulePredicate.From(predicate),
            action,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(createdAt));
        if (publish) rule.Publish(new FakeClock(createdAt.AddMinutes(1)));
        return rule;
    }

    private static EvaluationContext Context(string json) =>
        new(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Single_matching_rule_yields_one_SetVariableValue_effect()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(
            "rule-a",
            RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"),
            BaseMoment));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        RuleActionEffect.SetVariableValue effect =
            effects.ShouldHaveSingleItem().ShouldBeOfType<RuleActionEffect.SetVariableValue>();
        effect.Name.ShouldBe("oeeLine1");
        effect.Value.ShouldBe("46");
    }

    [Fact]
    public void Non_matching_predicate_yields_no_effects()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(
            "rule-a",
            RuleAction.SetVariableValue.From("oeeLine1", "100"),
            BaseMoment,
            predicate: "$.payload.cycleTime > 999"));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        effects.ShouldBeEmpty();
    }

    [Fact]
    public void HighlightOverlay_action_yields_an_overlay_effect()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(
            "rule-a",
            RuleAction.HighlightOverlay.From(overlay, 5_000),
            BaseMoment));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        RuleActionEffect.HighlightOverlay effect =
            effects.ShouldHaveSingleItem().ShouldBeOfType<RuleActionEffect.HighlightOverlay>();
        effect.Overlay.ShouldBe(overlay);
        effect.DurationMs.ShouldBe(5_000);
    }

    [Fact]
    public void Conflict_two_rules_writing_the_same_variable_emit_both_in_createdAt_order()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(
            "rule-a",
            RuleAction.SetVariableValue.From("oeeLine1", "50"),
            BaseMoment));
        cache.Upsert(ActiveRule(
            "rule-b",
            RuleAction.SetVariableValue.From("oeeLine1", "75"),
            BaseMoment.AddMinutes(5)));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        effects.Count.ShouldBe(2);
        // FR-012: last write wins per variable. Both effects are
        // emitted; the consumer applies them in order so rule-b's
        // value is the one that survives.
        effects[0].ShouldBeOfType<RuleActionEffect.SetVariableValue>().Value.ShouldBe("50");
        effects[1].ShouldBeOfType<RuleActionEffect.SetVariableValue>().Value.ShouldBe("75");
    }

    [Fact]
    public void Independent_two_rules_writing_different_variables_both_fire()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(
            "rule-a",
            RuleAction.SetVariableValue.From("oeeLine1", "82.5"),
            BaseMoment));
        cache.Upsert(ActiveRule(
            "rule-b",
            RuleAction.SetVariableValue.From("shiftStatus", "\"running\""),
            BaseMoment.AddMinutes(5)));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        effects.Count.ShouldBe(2);
        effects.OfType<RuleActionEffect.SetVariableValue>()
            .Select(e => e.Name).ShouldBe(["oeeLine1", "shiftStatus"]);
    }

    [Fact]
    public void Predicate_runtime_failure_on_one_rule_skips_just_that_rule()
    {
        InMemoryRuleCache cache = new();
        // The first rule has a predicate that returns non-bool, so
        // its evaluation must be skipped (not crash the loop).
        cache.Upsert(ActiveRule(
            "rule-bad",
            RuleAction.SetVariableValue.From("oeeLine1", "50"),
            BaseMoment,
            predicate: "1 + $.payload.cycleTime"));
        // The second rule is well-formed and should still fire.
        cache.Upsert(ActiveRule(
            "rule-good",
            RuleAction.SetVariableValue.From("oeeLine1", "99"),
            BaseMoment.AddMinutes(5)));

        RuleEvaluator evaluator = new(cache, NullLogger<RuleEvaluator>.Instance);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            "plc", "PlcCycleStart", Context(PlcCycleStartContext));

        effects.ShouldHaveSingleItem()
            .ShouldBeOfType<RuleActionEffect.SetVariableValue>().Value.ShouldBe("99");
    }
}
