using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class StreamStateTests
{
    [Theory]
    [InlineData("Provisioning")]
    [InlineData("Healthy")]
    [InlineData("Degraded")]
    [InlineData("Offline")]
    public void From_known_value_returns_the_matching_singleton(string value)
    {
        StreamState first = StreamState.From(value);
        StreamState second = StreamState.From(value);

        first.Value.ShouldBe(value);
        first.ShouldBeSameAs(second);
    }

    [Theory]
    [InlineData("healthy")]
    [InlineData("Active")]
    [InlineData("")]
    public void From_unknown_value_throws(string value)
    {
        Action act = () => StreamState.From(value);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_canonical_value()
    {
        StreamState.Healthy.ToString().ShouldBe("Healthy");
    }
}
