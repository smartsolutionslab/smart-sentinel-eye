using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.RegisteredEventType;

public class RegisteredEventTypeIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_identifier()
    {
        RegisteredEventTypeIdentifier id = RegisteredEventTypeIdentifier.New();
        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => RegisteredEventTypeIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        Guid guid = Guid.CreateVersion7();
        RegisteredEventTypeIdentifier.From(guid).Value.ShouldBe(guid);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = RegisteredEventTypeIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        RegisteredEventTypeIdentifier earlier = RegisteredEventTypeIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        RegisteredEventTypeIdentifier later = RegisteredEventTypeIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }

    [Fact]
    public void ToString_renders_the_underlying_guid()
    {
        Guid guid = Guid.CreateVersion7();
        RegisteredEventTypeIdentifier.From(guid).ToString().ShouldBe(guid.ToString());
    }
}
