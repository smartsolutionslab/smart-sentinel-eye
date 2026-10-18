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
    public void A_url_with_a_host_extracts_the_same_path_as_before(string url, string expected)
    {
        var extracted = RtspPath.TryExtract(url, out var path);

        extracted.ShouldBeTrue();
        path.ShouldBe(expected);
    }
}
