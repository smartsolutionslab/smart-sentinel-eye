using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class EventIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_strongly_typed_identifier()
    {
        var identifier = EventIdentifier.New();
        identifier.Value.ShouldNotBe(Guid.Empty);
        identifier.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => EventIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        var guid = Guid.CreateVersion7();
        var identifier = EventIdentifier.From(guid);
        identifier.Value.ShouldBe(guid);
    }

    [Fact]
    public void Identifiers_with_the_same_guid_are_equal()
    {
        var guid = Guid.CreateVersion7();
        EventIdentifier.From(guid).ShouldBe(EventIdentifier.From(guid));
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        var guid = Guid.CreateVersion7();
        Guid unwrapped = EventIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        var earlier = EventIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        var later = EventIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
