namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 392 / issues #3012 and #3017, ADR-0144), captured green on <c>de9802b1</c> before
/// <see cref="UrlRedaction.ToOrigin"/> learns to withhold a tail after the closing bracket. An empty host is
/// already the placeholder, a bracketed host with nothing but a port after it keeps its origin, and the userinfo
/// and UNC forms keep theirs. None of these rows may be edited by the change.
/// </summary>
public class UrlRedactionAuthorityHostCharacterisationTests
{
    [Theory]
    [InlineData("rtsp://")]
    [InlineData("rtsp:///")]
    [InlineData("rtsp:///cam")]
    [InlineData("RTSP:///cam")]
    [InlineData("rtsp:///admin:4242@cam/s")]
    [InlineData("rtsp:////admin:4242@cam/s")]
    [InlineData("rtsp://?x")]
    [InlineData("rtsp://#x")]
    [InlineData("rtsp:///?x")]
    public void An_empty_host_is_already_the_placeholder(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]/s", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[::1]:554/s", "rtsp://[::1]:554")]
    [InlineData("rtsp://[fe80::1]:554/s", "rtsp://[fe80::1]:554")]
    [InlineData("rtsp://[fe80::1]:0554/s", "rtsp://[fe80::1]:554")]
    [InlineData("rtsp://[fe80::1]:/s", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]?q", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]#f", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1/64%25eth0]/s", "rtsp://[fe80::1]")]
    [InlineData("rtsp://user:pass@[fd00::5]:8554/x", "rtsp://[fd00::5]:8554")]
    [InlineData("rtsp://[]@[fe80::1/64%25eth0]/s", "rtsp://[fe80::1]")]
    [InlineData("\\\\[fe80::1]\\x", "file://[fe80::1]")]
    [InlineData("file:////[fe80::1]/x", "file://[fe80::1]")]
    public void A_bracketed_host_with_nothing_but_a_port_after_it_keeps_its_origin(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }
}
