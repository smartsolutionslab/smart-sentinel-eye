using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class TranscodeModeTests
{
    [Theory]
    [InlineData("Passthrough")]
    [InlineData("Software")]
    [InlineData("Unknown")]
    public void From_known_value_returns_the_matching_singleton(string value)
    {
        TranscodeMode result = TranscodeMode.From(value);

        result.Value.ShouldBe(value);
        result.ShouldBeSameAs(TranscodeMode.From(value));
    }

    [Fact]
    public void From_unknown_value_throws()
    {
        Action act = () => TranscodeMode.From("Hardware");

        act.ShouldThrow<ArgumentException>();
    }
}
