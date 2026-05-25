using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

public class CameraIdentifierTests
{
    [Fact]
    public void New_returns_a_distinct_non_empty_identifier_each_time()
    {
        CameraIdentifier first = CameraIdentifier.New();
        CameraIdentifier second = CameraIdentifier.New();

        first.Value.ShouldNotBe(Guid.Empty);
        second.Value.ShouldNotBe(Guid.Empty);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void From_with_an_existing_guid_round_trips_the_value()
    {
        Guid raw = Guid.CreateVersion7();
        CameraIdentifier wrapped = CameraIdentifier.From(raw);

        wrapped.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_with_an_empty_guid_throws()
    {
        Action act = () => CameraIdentifier.From(Guid.Empty);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_guid_string_representation()
    {
        Guid raw = Guid.CreateVersion7();
        CameraIdentifier identifier = CameraIdentifier.From(raw);

        identifier.ToString().ShouldBe(raw.ToString());
    }
}
