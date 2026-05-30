using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

public class DeadLetterIdentifierTests
{
    [Fact]
    public void New_mints_Guid_v7()
    {
        DeadLetterIdentifier id = DeadLetterIdentifier.New();
        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => DeadLetterIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        Guid guid = Guid.CreateVersion7();
        DeadLetterIdentifier.From(guid).Value.ShouldBe(guid);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = DeadLetterIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        DeadLetterIdentifier earlier = DeadLetterIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        DeadLetterIdentifier later = DeadLetterIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
