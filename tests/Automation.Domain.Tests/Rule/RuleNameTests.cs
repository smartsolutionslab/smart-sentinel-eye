using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleNameTests
{
    [Theory]
    [InlineData("high-oee-on-fast-cycle")]
    [InlineData("rule-a")]
    [InlineData("ab")]
    public void Accepts_well_formed_kebab_lowercase_names(string raw) =>
        RuleName.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("a")]                          // too short
    [InlineData("HighOee")]                    // uppercase
    [InlineData("1bad")]                       // starts with digit
    [InlineData("with space")]                 // space
    [InlineData("under_score")]                // underscore
    public void Rejects_malformed_names(string raw)
    {
        Action act = () => RuleName.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_name()
    {
        Action act = () => RuleName.From(new string('a', RuleName.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }
}
