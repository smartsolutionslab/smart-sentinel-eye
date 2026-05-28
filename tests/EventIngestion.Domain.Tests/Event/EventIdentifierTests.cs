using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class EventIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_strongly_typed_identifier()
    {
        EventIdentifier identifier = EventIdentifier.New();
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
        Guid guid = Guid.CreateVersion7();
        EventIdentifier identifier = EventIdentifier.From(guid);
        identifier.Value.ShouldBe(guid);
    }

    [Fact]
    public void Identifiers_with_the_same_guid_are_equal()
    {
        Guid guid = Guid.CreateVersion7();
        EventIdentifier.From(guid).ShouldBe(EventIdentifier.From(guid));
    }
}
