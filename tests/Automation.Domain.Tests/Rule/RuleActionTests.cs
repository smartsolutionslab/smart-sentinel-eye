using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleActionTests
{
    [Fact]
    public void SetVariableValue_round_trips_a_target_name_and_value_expression()
    {
        RuleAction.SetVariableValue action = RuleAction.SetVariableValue.From(
            "oeeLine1", "100 - $.payload.cycleTime * 2");

        action.VariableName.ShouldBe("oeeLine1");
        action.ValueExpression.ShouldBe("100 - $.payload.cycleTime * 2");
    }

    [Theory]
    [InlineData("", "100")]
    [InlineData("oeeLine1", "")]
    public void SetVariableValue_rejects_empty_fields(string varName, string expr)
    {
        Action act = () => RuleAction.SetVariableValue.From(varName, expr);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void HighlightOverlay_round_trips_overlay_and_duration()
    {
        Guid overlay = Guid.CreateVersion7();
        RuleAction.HighlightOverlay action = RuleAction.HighlightOverlay.From(overlay, 10_000);

        action.Overlay.ShouldBe(overlay);
        action.DurationMs.ShouldBe(10_000);
    }

    [Fact]
    public void HighlightOverlay_rejects_empty_overlay()
    {
        Action act = () => RuleAction.HighlightOverlay.From(Guid.Empty, 5_000);
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]                 // below 500ms minimum
    [InlineData(60_001)]               // above 60s maximum
    [InlineData(int.MinValue)]
    public void HighlightOverlay_rejects_durations_outside_500_to_60000_ms(int duration)
    {
        Action act = () => RuleAction.HighlightOverlay.From(Guid.CreateVersion7(), duration);
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(10_000)]
    [InlineData(60_000)]
    public void HighlightOverlay_accepts_boundary_durations(int duration)
    {
        RuleAction.HighlightOverlay action = RuleAction.HighlightOverlay.From(
            Guid.CreateVersion7(), duration);
        action.DurationMs.ShouldBe(duration);
    }

    [Fact]
    public void Actions_with_the_same_payload_are_equal()
    {
        Guid overlay = Guid.CreateVersion7();
        RuleAction a = RuleAction.HighlightOverlay.From(overlay, 5_000);
        RuleAction b = RuleAction.HighlightOverlay.From(overlay, 5_000);
        a.ShouldBe(b);
    }
}
