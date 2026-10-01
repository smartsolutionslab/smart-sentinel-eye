using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Persistence.Configurations;

namespace SmartSentinelEye.Automation.Infrastructure.Tests.Persistence.Configurations;

/// <summary>
/// Spec 296 FR-002: <c>RuleActionColumnConverter</c> packs/unpacks
/// <c>RuleAction.SwitchWallScene</c> as <c>SwitchWallScene|&lt;wall:D&gt;|Next</c>
/// or <c>SwitchWallScene|&lt;wall:D&gt;|Layout|&lt;layout:D&gt;</c> — the same
/// leading-discriminator scheme <c>SetVariableValue</c> and
/// <c>HighlightOverlay</c> already use. No prior unit test exercised this
/// internal converter directly (its round-trip was previously provable only
/// through a real database); this is a new file, not an extension of one.
/// </summary>
public class RuleActionColumnConverterTests
{
    [Fact]
    public void Next_target_round_trips_through_the_packed_column()
    {
        Guid wall = Guid.CreateVersion7();
        RuleAction original = RuleAction.SwitchWallScene.From(wall, "Next", null);

        string packed = RuleActionColumnConverter.ToColumn(original);
        RuleAction roundTripped = RuleActionColumnConverter.FromColumn(packed);

        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void Layout_target_round_trips_through_the_packed_column()
    {
        Guid wall = Guid.CreateVersion7();
        Guid layout = Guid.CreateVersion7();
        RuleAction original = RuleAction.SwitchWallScene.From(wall, "Layout", layout);

        string packed = RuleActionColumnConverter.ToColumn(original);
        RuleAction roundTripped = RuleActionColumnConverter.FromColumn(packed);

        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void FromColumn_rejects_a_malformed_wall_guid()
    {
        Action act = () => RuleActionColumnConverter.FromColumn("SwitchWallScene|not-a-guid|Next");
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromColumn_rejects_a_malformed_layout_guid()
    {
        Guid wall = Guid.CreateVersion7();
        Action act = () => RuleActionColumnConverter.FromColumn(
            $"SwitchWallScene|{wall:D}|Layout|not-a-guid");
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromColumn_rejects_an_unknown_target()
    {
        Guid wall = Guid.CreateVersion7();
        Action act = () => RuleActionColumnConverter.FromColumn(
            $"SwitchWallScene|{wall:D}|Sideways");
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromColumn_rejects_Layout_with_no_layout_segment()
    {
        Guid wall = Guid.CreateVersion7();
        Action act = () => RuleActionColumnConverter.FromColumn($"SwitchWallScene|{wall:D}|Layout");
        act.ShouldThrow<ArgumentException>();
    }
}
