using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

/// <summary>
/// T002 (spec 317, #2325) — FR-001. A closed three-value VO, mirroring
/// <c>RegistrationState</c> / <c>EventTypeMode</c> (plan.md §2.2).
/// </summary>
public class DeadLetterReasonTests
{
    [Theory]
    [InlineData("ParseFailure")]
    [InlineData("Refused")]
    [InlineData("UnknownEventType")]
    public void From_parses_each_known_value(string value)
    {
        DeadLetterReason.From(value).Value.ShouldBe(value);
    }

    [Fact]
    public void From_throws_on_an_unknown_value()
    {
        Should.Throw<ArgumentException>(() => DeadLetterReason.From("SomethingElse"));
    }

    [Fact]
    public void The_singletons_are_the_values_From_parses()
    {
        DeadLetterReason.ParseFailure.ShouldBe(DeadLetterReason.From("ParseFailure"));
        DeadLetterReason.Refused.ShouldBe(DeadLetterReason.From("Refused"));
        DeadLetterReason.UnknownEventType.ShouldBe(DeadLetterReason.From("UnknownEventType"));
    }

    [Fact]
    public void ToString_returns_the_wire_value()
    {
        DeadLetterReason.UnknownEventType.ToString().ShouldBe("UnknownEventType");
    }
}
