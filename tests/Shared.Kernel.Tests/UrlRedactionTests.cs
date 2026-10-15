namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>Spec 373 / issue #2908. A URL is rendered for logs as scheme://host[:port] only.</summary>
public class UrlRedactionTests
{
    [Theory]
    [InlineData("rtsp://10.0.5.5/h264", "rtsp://10.0.5.5")]
    [InlineData("rtsp://cam/stream?token=SECRET", "rtsp://cam")]
    [InlineData("rtsp://cam/SECRET/path", "rtsp://cam")]
    [InlineData("rtsp://cam:8554/x?y=z#frag", "rtsp://cam:8554")]
    [InlineData("rtsp://user:pass@cam/x", "rtsp://cam")]
    [InlineData("rtsp://[fd00::5]:554/x", "rtsp://[fd00::5]:554")]
    [InlineData("rtsp://CAM.Example/x", "rtsp://cam.example")]
    [InlineData("rtsp://cam", "rtsp://cam")]
    public void A_url_is_rendered_as_its_origin(string url, string expected) =>
        UrlRedaction.ToOrigin(url).ShouldBe(expected);

    [Theory]
    [InlineData("not a url SECRET")]
    [InlineData("/relative/SECRET")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unparseable_input_renders_the_placeholder_and_never_echoes_the_input(string url)
    {
        var rendered = UrlRedaction.ToOrigin(url);

        rendered.ShouldBe(UrlRedaction.Unparseable);
        rendered.ShouldNotContain("SECRET");
    }

    [Fact]
    public void A_null_input_is_refused() =>
        Should.Throw<ArgumentNullException>(() => UrlRedaction.ToOrigin(null!));
}
