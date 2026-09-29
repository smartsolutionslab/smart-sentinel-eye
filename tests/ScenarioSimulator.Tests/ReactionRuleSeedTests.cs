using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B04 (ADR-0144 red). <c>ReactionRuleSeed</c> and the
/// scenario-file types <c>ReactionDefinition</c> / <c>ReactionTrigger</c> /
/// <c>ReactionAction</c> do not exist yet; this class is the target shape
/// (plan.md §3.3, §3.4, T-B14).
///
/// <para>
/// <c>ReactionRuleSeed.From(scenario, asset, reaction, overlay)</c> builds a
/// <see cref="RuleSeed"/> the same way <see cref="HighlightRuleSeed"/> does for
/// the legacy field, or refuses it — it never throws. Unlike
/// <see cref="HighlightRuleSeed"/> the trigger source is taken verbatim from
/// <c>reaction.When.Source</c> (a reaction states its source; nothing is
/// derived from a matching sensor). In PR-B only <c>HighlightOverlay</c> is a
/// recognised action <c>Type</c> — <c>SetVariableValue</c> arrives in PR-C
/// (T-C03) and is refused here exactly like any other unrecognised type.
/// </para>
/// </summary>
public sealed class ReactionRuleSeedTests
{
    private static readonly AssetDefinition Asset = new()
    {
        Key = "station-4-roughing",
        Name = "Station 4",
        Camera = new CameraDefinition { Path = "station-4-roughing", Clip = "mill-roughing.mp4" },
        Overlay = new OverlayDefinition { Label = "ROUGHING", X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18 },
    };

    private static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public void The_predicate_is_wrapped_in_the_devices_own_guard_with_the_declared_predicate_parenthesised()
    {
        ReactionDefinition reaction = Reaction(
            "person-in-exclusion",
            "inference", "ObjectDetected",
            "$.payload.class == 'person' && $.payload.confidence >= 0.8",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        ReactionSeedResult.Valid valid = result.ShouldBeOfType<ReactionSeedResult.Valid>();
        valid.Seed.Predicate.ShouldBe(
            "$.device == 'station-4-roughing' && ($.payload.class == 'person' && $.payload.confidence >= 0.8)");
    }

    [Fact]
    public void A_declared_OR_stays_inside_the_device_guards_parentheses()
    {
        ReactionDefinition reaction = Reaction(
            "either-class",
            "inference", "ObjectDetected",
            "$.payload.class == 'person' || $.payload.class == 'forklift'",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        ReactionSeedResult.Valid valid = result.ShouldBeOfType<ReactionSeedResult.Valid>();
        valid.Seed.Predicate.ShouldBe(
            "$.device == 'station-4-roughing' && ($.payload.class == 'person' || $.payload.class == 'forklift')");
    }

    [Fact]
    public void The_rule_is_named_scenario_asset_reaction()
    {
        ReactionDefinition reaction = Reaction(
            "person-in-exclusion", "inference", "ObjectDetected", "$.payload.class == 'person'",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        ReactionSeedResult.Valid valid = result.ShouldBeOfType<ReactionSeedResult.Valid>();
        valid.Seed.Name.ShouldBe("rolling-mill-station-4-roughing-person-in-exclusion");
        valid.Seed.TriggerSource.ShouldBe("inference");
        valid.Seed.TriggerKind.ShouldBe("ObjectDetected");
    }

    [Fact]
    public void A_resulting_rule_name_longer_than_63_characters_is_refused()
    {
        ReactionDefinition reaction = Reaction(
            new string('x', 40), // "rolling-mill-station-4-roughing-" (32 chars) + 40 x's > 63
            "inference", "ObjectDetected", "$.payload.class == 'person'",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        result.ShouldBeOfType<ReactionSeedResult.Refused>();
    }

    [Fact]
    public void A_reaction_name_containing_characters_outside_the_rule_name_grammar_is_refused()
    {
        ReactionDefinition reaction = Reaction(
            "Person In Exclusion", // uppercase + spaces, not [a-z0-9-]
            "inference", "ObjectDetected", "$.payload.class == 'person'",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        result.ShouldBeOfType<ReactionSeedResult.Refused>();
    }

    [Fact]
    public void A_HighlightOverlay_reaction_on_an_asset_with_no_seeded_overlay_is_refused()
    {
        ReactionDefinition reaction = Reaction(
            "person-in-exclusion", "inference", "ObjectDetected", "$.payload.class == 'person'",
            "HighlightOverlay", durationMs: 4_000);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, overlay: null);

        result.ShouldBeOfType<ReactionSeedResult.Refused>();
    }

    [Fact]
    public void An_unrecognised_action_type_is_refused()
    {
        ReactionDefinition reaction = Reaction(
            "zone-state", "inference", "ObjectDetected", "$.payload.class == 'person'",
            "SwitchWallScene", durationMs: null);

        ReactionSeedResult result = ReactionRuleSeed.From("rolling-mill", Asset, reaction, Overlay);

        result.ShouldBeOfType<ReactionSeedResult.Refused>();
    }

    private static ReactionDefinition Reaction(
        string name, string source, string kind, string predicate, string actionType, int? durationMs) =>
        new()
        {
            Name = name,
            When = new ReactionTrigger { Source = source, Kind = kind, Predicate = predicate },
            Then = new ReactionAction { Type = actionType, DurationMs = durationMs },
        };
}
