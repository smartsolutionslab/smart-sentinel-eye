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

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = StreamIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        StreamIdentifier earlier = StreamIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        StreamIdentifier later = StreamIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
