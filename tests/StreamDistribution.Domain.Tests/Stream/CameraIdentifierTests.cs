using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class CameraIdentifierTests
{
    [Fact]
    public void From_with_a_non_empty_Guid_succeeds()
    {
        Guid raw = Guid.CreateVersion7();
        CameraIdentifier camera = CameraIdentifier.From(raw);

        camera.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_with_Guid_Empty_throws()
    {
        Action act = () => CameraIdentifier.From(Guid.Empty);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Two_identifiers_with_the_same_Guid_are_equal()
    {
        Guid raw = Guid.CreateVersion7();
        CameraIdentifier first = CameraIdentifier.From(raw);
        CameraIdentifier second = CameraIdentifier.From(raw);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
