using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class CameraIdentifierTests
{
    [Fact]
    public void From_with_a_non_empty_Guid_succeeds()
    {
        var raw = Guid.CreateVersion7();
        var camera = CameraIdentifier.From(raw);

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
        var raw = Guid.CreateVersion7();
        var first = CameraIdentifier.From(raw);
        var second = CameraIdentifier.From(raw);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        var guid = Guid.CreateVersion7();
        Guid unwrapped = CameraIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        var earlier = CameraIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        var later = CameraIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
