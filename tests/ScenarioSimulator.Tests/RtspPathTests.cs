using SmartSentinelEye.ScenarioSimulator.CameraSim;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 406 / #3025. The extracted path is interpolated into the camera-sim config API route, so a
/// URL without an authority (which keeps its dot segments) must never yield one, while every URL
/// with a host extracts exactly what it always did.
/// </summary>
public sealed class RtspPathTests
{
    [Theory]
    [InlineData("rtsp:../../../../SECRET-token")]
    [InlineData("rtsp:../SECRET/x")]
    [InlineData("rtsp:/../../../../SECRET-token")]
    [InlineData("rtsp:SECRET/x")]
    [InlineData("rtsp:/SECRET/x")]
    [InlineData("rtsp:///SECRET/x")]
    [InlineData("rtsp:..")]
    [InlineData("file:///../../SECRET")]
    [InlineData("urn:x:SECRET")]
    [InlineData("rtsp://[::1]../../../../SECRET-token")]
    [InlineData("rtsp://[::1]%2E%2E/%2E%2E/SECRET")]
    [InlineData("rtsp://[::1]../replace/x")]
    [InlineData("rtsp://[::1]../::1//:x.SECRET")]
    [InlineData("rtsp://[::1]..")]
    [InlineData("rtsp://[::1]..?q")]
    [InlineData("rtsp://[::1%25]..#SECRET")]
    [InlineData("rtsp://[::1].")]
    [InlineData("rtsp://[::1]%2E#SECRET")]
    [InlineData("rtsp://[::1]..//SECRET")]
    [InlineData("mailto:x@h/../../../../SECRET-token")]
    [InlineData("ldap://h/../../../../SECRET-token")]
    [InlineData("nntp://h/../../../../SECRET-token")]
    [InlineData("telnet://h/../../../../SECRET-token")]
    [InlineData("gopher://h/../../../../SECRET-token")]
    [InlineData("rtsp://[::1]../add/x")]
    [InlineData("rtsp://[::1]%2E%2E/add/x")]
    [InlineData("mailto:x@h/../add/x")]
    [InlineData("ldap://h/../add/x")]
    public void A_url_that_can_leave_the_config_prefix_is_not_a_simulated_camera_path(string url)
    {
        var extracted = RtspPath.TryExtract(url, out var path);

        extracted.ShouldBeFalse();
        path.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("rtsp://camera-sim:8554/station-4-roughing", "station-4-roughing")]
    [InlineData("rtsp://camera-sim:8554/electronics-moulding", "electronics-moulding")]
    [InlineData("rtsp://camera-sim:8554/paper-press-group", "paper-press-group")]
    [InlineData("rtsp://cam/SECRET-token/stream", "SECRET-token/stream")]
    [InlineData("rtsp://cam/a b/c", "a%20b/c")]
    [InlineData("rtsp://cam/caf%C3%A9", "caf%C3%A9")]
    [InlineData("rtsp://cam/../../../../SECRET", "SECRET")]
    [InlineData("rtsp://cam/../x", "x")]
    [InlineData("rtsp://cam/a/%2E%2E/%2E%2E/%2E%2E/%2E%2E/SECRET", "SECRET")]
    [InlineData("rtsp://cam/.%2E/.%2E/.%2E/.%2E/SECRET", "SECRET")]
    [InlineData("rtsp://cam/a%2F..%2F..%2F..%2F..%2FSECRET", "a%2F..%2F..%2F..%2F..%2FSECRET")]
    [InlineData("rtsp://[fe80::1%25eth0]:8554/P", "P")]
    [InlineData("rtsp://[::1]/x", "x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x/y", "x/y")]
    [InlineData("rtsp://h:8554/a/b", "a/b")]
    [InlineData("RTSP://h/x", "x")]
    [InlineData("rtsp://h/a:b/c", "a:b/c")]
    [InlineData("rtsp://h/ch1:main", "ch1:main")]
    [InlineData("rtsp://h/c:/x", "c:/x")]
    [InlineData("rtsp://h/evil.com:x", "evil.com:x")]
    [InlineData("rtsp://h/http://evil.com/x", "http://evil.com/x")]
    public void A_url_with_a_host_extracts_the_same_path_as_before(string url, string expected)
    {
        var extracted = RtspPath.TryExtract(url, out var path);

        extracted.ShouldBeTrue();
        path.ShouldBe(expected);
    }
}
