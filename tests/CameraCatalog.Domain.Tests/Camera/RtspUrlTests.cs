using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

public class RtspUrlTests
{
    [Fact]
    public void From_with_a_valid_rtsp_url_returns_the_value()
    {
        RtspUrl url = RtspUrl.From("rtsp://10.0.5.12:554/h264");

        url.Value.ShouldBe("rtsp://10.0.5.12:554/h264");
    }

    [Theory]
    [InlineData("", "empty string")]
    [InlineData("   ", "whitespace")]
    [InlineData("http://10.0.5.12/h264", "http scheme")]
    [InlineData("https://10.0.5.12/h264", "https scheme")]
    [InlineData("file://camera.local/stream", "file scheme")]
    public void From_with_invalid_input_fails(string input, string reason)
    {
        Action act = () => RtspUrl.From(input);

        act.ShouldThrow<ArgumentException>($"because {reason} is invalid");
    }

    [Fact]
    public void From_with_a_userinfo_segment_fails()
    {
        Action act = () => RtspUrl.From("rtsp://admin:hunter2@10.0.5.12/h264");

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_with_a_url_longer_than_the_maximum_fails()
    {
        string tooLong = "rtsp://" + new string('x', RtspUrl.MaximumLength);

        Action act = () => RtspUrl.From(tooLong);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_accepts_an_uppercase_scheme_prefix()
    {
        // RFC 7826 lets schemes be case-insensitive. We preserve original casing.
        RtspUrl url = RtspUrl.From("RTSP://camera.local/stream");

        url.Value.ShouldBe("RTSP://camera.local/stream");
    }
}
