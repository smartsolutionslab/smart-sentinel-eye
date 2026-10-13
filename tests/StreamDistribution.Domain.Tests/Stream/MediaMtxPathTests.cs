using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class MediaMtxPathTests
{
    [Fact]
    public void For_camera_produces_cam_dash_guid()
    {
        var raw = Guid.CreateVersion7();
        var camera = CameraIdentifier.From(raw);

        var path = MediaMtxPath.For(camera);

        path.Value.ShouldBe($"cam-{raw}");
    }

    [Fact]
    public void From_rejects_strings_that_do_not_match_the_pattern()
    {
        Action act = () => MediaMtxPath.From("camera-123");

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_accepts_the_cam_dash_guid_format()
    {
        var raw = Guid.CreateVersion7();

        var path = MediaMtxPath.From($"cam-{raw}");

        path.Value.ShouldBe($"cam-{raw}");
    }

    [Fact]
    public void Two_paths_with_the_same_value_are_equal()
    {
        var raw = Guid.CreateVersion7();
        var first = MediaMtxPath.From($"cam-{raw}");
        var second = MediaMtxPath.From($"cam-{raw}");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
