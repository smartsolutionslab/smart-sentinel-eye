using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class KindTests
{
    [Theory]
    [InlineData("PlcCycleStart")]
    [InlineData("PersonInRestrictedZone")]
    [InlineData("Annotation")]
    [InlineData("Webhook")]
    [InlineData("E")]
    public void Accepts_PascalCase_identifiers(string raw)
    {
        var kind = Kind.From(raw);
        kind.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plcCycleStart")]              // lowercase first letter
    [InlineData("Plc_Cycle_Start")]            // underscore
    [InlineData("Plc-Cycle")]                  // hyphen
    [InlineData("Plc Cycle")]                  // space
    public void Rejects_malformed_kinds(string raw)
    {
        Action act = () => Kind.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_kind()
    {
        var overlong = "A" + new string('b', Kind.MaximumLength);
        Action act = () => Kind.From(overlong);
        act.ShouldThrow<ArgumentException>();
    }
}
