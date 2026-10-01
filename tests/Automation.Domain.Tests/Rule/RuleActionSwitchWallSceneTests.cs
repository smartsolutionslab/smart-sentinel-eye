using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

/// <summary>
/// Spec 296 FR-001/FR-003 (US2-7): <c>RuleAction.SwitchWallScene</c> is the
/// third <c>RuleAction</c> variant. <c>From</c> parses the API edge's raw
/// <c>wallIdentifier</c>/<c>sceneTarget</c>/<c>targetLayoutIdentifier</c>
/// triple, throwing <see cref="ArgumentException"/> for every bad shape
/// US2-7 enumerates — the same error <c>RulesEndpoints</c> already maps to
/// <c>400 RULE_INVALID_INPUT</c> (spec §1.8), so no new error type is
/// needed. Mirrors <c>RuleActionTests</c>' coverage style for the two
/// existing variants.
/// </summary>
public class RuleActionSwitchWallSceneTests
{
    [Fact]
    public void From_with_target_Next_and_no_layout_round_trips_the_wall_and_Next_target()
    {
        Guid wall = Guid.CreateVersion7();
        RuleAction.SwitchWallScene action = RuleAction.SwitchWallScene.From(wall, "Next", null);

        action.Wall.Value.ShouldBe(wall);
        action.Target.ShouldBeOfType<SceneTarget.Next>();
    }

    [Fact]
    public void From_with_target_Layout_and_a_layout_round_trips_the_wall_and_layout_target()
    {
        Guid wall = Guid.CreateVersion7();
        Guid layout = Guid.CreateVersion7();
        RuleAction.SwitchWallScene action = RuleAction.SwitchWallScene.From(wall, "Layout", layout);

        action.Wall.Value.ShouldBe(wall);
        SceneTarget.Layout target = action.Target.ShouldBeOfType<SceneTarget.Layout>();
        target.Value.Value.ShouldBe(layout);
    }

    [Fact]
    public void From_rejects_an_empty_wall_guid()
    {
        Action act = () => RuleAction.SwitchWallScene.From(Guid.Empty, "Next", null);
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("next")]
    [InlineData("Layout ")]
    [InlineData("Highlight")]
    public void From_rejects_a_target_that_is_not_exactly_Next_or_Layout(string? target)
    {
        Action act = () => RuleAction.SwitchWallScene.From(Guid.CreateVersion7(), target!, null);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_target_Layout_with_no_targetLayoutIdentifier()
    {
        Action act = () => RuleAction.SwitchWallScene.From(Guid.CreateVersion7(), "Layout", null);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_target_Next_with_a_targetLayoutIdentifier()
    {
        Action act = () => RuleAction.SwitchWallScene.From(
            Guid.CreateVersion7(), "Next", Guid.CreateVersion7());
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_an_empty_layout_guid()
    {
        Action act = () => RuleAction.SwitchWallScene.From(
            Guid.CreateVersion7(), "Layout", Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Actions_with_the_same_wall_and_Next_target_are_equal()
    {
        Guid wall = Guid.CreateVersion7();
        RuleAction a = RuleAction.SwitchWallScene.From(wall, "Next", null);
        RuleAction b = RuleAction.SwitchWallScene.From(wall, "Next", null);
        a.ShouldBe(b);
    }

    [Fact]
    public void Actions_with_the_same_wall_and_layout_target_are_equal()
    {
        Guid wall = Guid.CreateVersion7();
        Guid layout = Guid.CreateVersion7();
        RuleAction a = RuleAction.SwitchWallScene.From(wall, "Layout", layout);
        RuleAction b = RuleAction.SwitchWallScene.From(wall, "Layout", layout);
        a.ShouldBe(b);
    }

    [Fact]
    public void Actions_with_different_walls_are_not_equal()
    {
        RuleAction a = RuleAction.SwitchWallScene.From(Guid.CreateVersion7(), "Next", null);
        RuleAction b = RuleAction.SwitchWallScene.From(Guid.CreateVersion7(), "Next", null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Actions_with_the_same_wall_but_different_targets_are_not_equal()
    {
        Guid wall = Guid.CreateVersion7();
        RuleAction a = RuleAction.SwitchWallScene.From(wall, "Next", null);
        RuleAction b = RuleAction.SwitchWallScene.From(wall, "Layout", Guid.CreateVersion7());
        a.ShouldNotBe(b);
    }

    [Fact]
    public void SwitchWallScene_is_not_equal_to_the_other_RuleAction_variants()
    {
        RuleAction switchWallScene = RuleAction.SwitchWallScene.From(Guid.CreateVersion7(), "Next", null);
        RuleAction highlight = RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), 5_000);

        switchWallScene.ShouldNotBe(highlight);
    }
}
