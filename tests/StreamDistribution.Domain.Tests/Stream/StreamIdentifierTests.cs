using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class StreamIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_Guid_v7()
    {
        StreamIdentifier identifier = StreamIdentifier.New();

        identifier.Value.ShouldNotBe(Guid.Empty);
        identifier.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_with_a_non_empty_Guid_succeeds()
    {
        Guid raw = Guid.CreateVersion7();
        StreamIdentifier identifier = StreamIdentifier.From(raw);

        identifier.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_with_Guid_Empty_throws()
    {
        Action act = () => StreamIdentifier.From(Guid.Empty);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_Guid_string()
    {
        Guid raw = Guid.CreateVersion7();
        StreamIdentifier identifier = StreamIdentifier.From(raw);

        identifier.ToString().ShouldBe(raw.ToString());
    }
}
