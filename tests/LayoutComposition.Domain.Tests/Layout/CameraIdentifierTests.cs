using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class CameraIdentifierTests
{
    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        var raw = Guid.CreateVersion7();
        var camera = CameraIdentifier.From(raw);
        camera.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => CameraIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
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
