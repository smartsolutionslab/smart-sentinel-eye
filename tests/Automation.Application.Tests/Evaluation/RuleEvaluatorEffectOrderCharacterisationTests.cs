using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Evaluation;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before the per-rule <c>switch</c> in
/// <c>RuleEvaluator.Evaluate</c> moves into a helper. This is the
/// event-to-overlay-state leg (constitution section IV): the helper must not
/// reorder effects, drop the "skip and carry on" behaviour, or change what a
/// skipped rule logs. <c>RuleEvaluatorTests</c> pins each action alone and two
/// of a kind together; none mixes the three actions with a rule that throws and
/// a rule whose action nobody handles, and none reads the log text.
/// </summary>
public class RuleEvaluatorEffectOrderCharacterisationTests
{
    private const string Context = """
        {
          "source": "plc",
          "kind": "PlcCycleStart",
          "device": "station-4",
          "payload": { "cycleTime": 27 }
        }
        """;

    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly Guid Overlay = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    private static readonly Guid FirstWall = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid SecondWall = Guid.Parse("00000000-0000-7000-8000-0000000000b2");

    private static readonly Guid TargetLayout = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    private static RuleAggregate ActiveRule(
        int minutesAfterBase,
        RuleAction action,
        string predicate = "$.payload.cycleTime <= 30")
    {
        var createdAt = BaseMoment.AddMinutes(minutesAfterBase);
        var rule = new RuleBuilder()
            .WithName($"rule-{minutesAfterBase}")
            .WithPredicate(predicate)
            .WithAction(action)
            .WithClock(createdAt)
            .Build();
        rule.Publish(new FakeClock(createdAt.AddSeconds(1)));
        return rule;
    }

    private static EvaluationContext Parsed() => new(JsonDocument.Parse(Context).RootElement);

    private static IReadOnlyList<RuleActionEffect> Evaluate(
        InMemoryRuleCache cache,
        CapturingLogger<RuleEvaluator> logger) =>
        new RuleEvaluator(cache, logger).Evaluate(
            FabIdentifier.From("munich"),
            "plc",
            "PlcCycleStart",
            Parsed());

    [Fact]
    public void Mixed_rules_yield_their_effects_in_creation_order_and_skipped_rules_leave_a_log()
    {
        InMemoryRuleCache cache = new();
        var highlight = ActiveRule(0, RuleAction.HighlightOverlay.From(Overlay, 5_000));
        var setVariable = ActiveRule(1, RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2"));
        var throwingValue = ActiveRule(2, RuleAction.SetVariableValue.From("oeeLine2", "1 / 0"));
        var unhandled = ActiveRule(3, new UnhandledAction());
        var switchNext = ActiveRule(4, RuleAction.SwitchWallScene.From(FirstWall, "Next", null));
        var switchLayout = ActiveRule(5, RuleAction.SwitchWallScene.From(SecondWall, "Layout", TargetLayout));
        var setDevice = ActiveRule(6, RuleAction.SetVariableValue.From("station", "$.device"));
        foreach (var rule in new[] { setDevice, switchLayout, unhandled, highlight, throwingValue, setVariable, switchNext })
        {
            cache.Upsert(rule);
        }

        CapturingLogger<RuleEvaluator> logger = new();

        var effects = Evaluate(cache, logger);

        effects.ShouldBe(
        [
            new RuleActionEffect.HighlightOverlay(Overlay, 5_000),
            new RuleActionEffect.SetVariableValue("oeeLine1", "46"),
            new RuleActionEffect.SwitchWallScene(FirstWall, "Next", null, switchNext.Id.Value),
            new RuleActionEffect.SwitchWallScene(SecondWall, "Layout", TargetLayout, switchLayout.Id.Value),
            new RuleActionEffect.SetVariableValue("station", "station-4"),
        ]);
        logger.Entries.Count.ShouldBe(2);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe(
            $"Value-expression evaluation failed on rule {throwingValue.Id}; skipping action.");
        logger.Entries[0].Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("division by zero");
        logger.Entries[1].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[1].Message.ShouldBe($"Unhandled RuleAction case UnhandledAction on rule {unhandled.Id}.");
        logger.Entries[1].Exception.ShouldBeNull();
    }

    [Fact]
    public void A_rule_whose_predicate_throws_is_logged_and_skipped_and_the_next_rule_still_runs()
    {
        InMemoryRuleCache cache = new();
        var broken = ActiveRule(
            0,
            RuleAction.HighlightOverlay.From(Overlay, 1_000),
            predicate: "1 / 0 == 1");
        var healthy = ActiveRule(1, RuleAction.HighlightOverlay.From(Overlay, 2_000));
        cache.Upsert(broken);
        cache.Upsert(healthy);
        CapturingLogger<RuleEvaluator> logger = new();

        var effects = Evaluate(cache, logger);

        effects.ShouldBe([new RuleActionEffect.HighlightOverlay(Overlay, 2_000)]);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe($"Predicate evaluation failed on rule {broken.Id}; skipping rule.");
        logger.Entries[0].Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("division by zero");
    }

    [Fact]
    public void A_predicate_that_is_not_true_skips_the_rule_without_a_log()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(0, RuleAction.HighlightOverlay.From(Overlay, 1_000), predicate: "$.payload.cycleTime > 30"));
        cache.Upsert(ActiveRule(1, RuleAction.HighlightOverlay.From(Overlay, 2_000), predicate: "7"));
        cache.Upsert(ActiveRule(2, RuleAction.HighlightOverlay.From(Overlay, 3_000), predicate: "$.payload.missing"));
        CapturingLogger<RuleEvaluator> logger = new();

        var effects = Evaluate(cache, logger);

        effects.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void No_candidates_for_the_trigger_yield_nothing_after_exactly_one_lookup()
    {
        InMemoryRuleCache cache = new();
        cache.Upsert(ActiveRule(0, RuleAction.HighlightOverlay.From(Overlay, 1_000)));
        CapturingLogger<RuleEvaluator> logger = new();

        var effects = new RuleEvaluator(cache, logger).Evaluate(
            FabIdentifier.From("munich"),
            "plc",
            "SomethingElse",
            Parsed());

        effects.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
        cache.Lookups.ShouldBe([("munich", "plc", "SomethingElse")]);
    }

    private sealed record UnhandledAction : RuleAction;
}
