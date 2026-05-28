using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RulePredicateTests
{
    [Theory]
    [InlineData("$.payload.cycleTime > 30")]
    [InlineData("$.kind == \"PlcCycleStart\"")]
    [InlineData("true")]
    public void Accepts_a_non_empty_bounded_string(string raw) =>
        RulePredicate.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_or_whitespace(string raw)
    {
        Action act = () => RulePredicate.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_predicate()
    {
        Action act = () => RulePredicate.From(new string('a', RulePredicate.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }
}
