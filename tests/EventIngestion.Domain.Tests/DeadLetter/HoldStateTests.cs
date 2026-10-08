using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

/// <summary>
/// T002 (spec 317, #2325) — FR-003. A closed two-value VO, mirroring
/// <c>RegistrationState</c> (plan.md §2.2).
/// </summary>
public class HoldStateTests
{
    [Theory]
    [InlineData("Held")]
    [InlineData("Promoted")]
    public void From_parses_each_known_value(string value)
    {
        HoldState.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void From_throws_on_an_unknown_value()
    {
        Should.Throw<ArgumentException>(() => HoldState.From("Released"));
    }

    [Fact]
    public void The_singletons_are_the_values_From_parses()
    {
        HoldState.Held.ShouldBe(HoldState.From("Held"));
        HoldState.Promoted.ShouldBe(HoldState.From("Promoted"));
    }

    [Fact]
    public void ToString_returns_the_wire_value()
    {
        HoldState.Promoted.ToString().ShouldBe("Promoted");
    }
}
