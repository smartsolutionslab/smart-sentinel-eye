using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleStateTests
{
    [Fact]
    public void Exposes_three_singletons_with_PascalCase_wire_strings()
    {
        RuleState.Draft.Value.ShouldBe("Draft");
        RuleState.Active.Value.ShouldBe("Active");
        RuleState.Archived.Value.ShouldBe("Archived");
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Active")]
    [InlineData("Archived")]
    public void From_round_trips_each_known_value(string raw) =>
        RuleState.From(raw).Value.ShouldBe(raw);

    [Fact]
    public void From_returns_the_singleton_for_each_known_value()
    {
        RuleState.From("Draft").ShouldBeSameAs(RuleState.Draft);
        RuleState.From("Active").ShouldBeSameAs(RuleState.Active);
        RuleState.From("Archived").ShouldBeSameAs(RuleState.Archived);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("")]
    [InlineData("Deleted")]
    public void From_rejects_unknown_strings(string raw)
    {
        Action act = () => RuleState.From(raw);
        act.ShouldThrow<ArgumentException>();
    }
}
