using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class MediaMtxPathTests
{
    [Fact]
    public void For_camera_produces_cam_dash_guid()
    {
        Guid raw = Guid.CreateVersion7();
        CameraIdentifier camera = CameraIdentifier.From(raw);

        MediaMtxPath path = MediaMtxPath.For(camera);

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
        Guid raw = Guid.CreateVersion7();

        MediaMtxPath path = MediaMtxPath.From($"cam-{raw}");

        path.Value.ShouldBe($"cam-{raw}");
    }

    [Fact]
    public void Two_paths_with_the_same_value_are_equal()
    {
        Guid raw = Guid.CreateVersion7();
        MediaMtxPath first = MediaMtxPath.From($"cam-{raw}");
        MediaMtxPath second = MediaMtxPath.From($"cam-{raw}");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
