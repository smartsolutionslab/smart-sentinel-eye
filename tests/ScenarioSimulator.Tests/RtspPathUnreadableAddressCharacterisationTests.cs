using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 408 / #3031 item 2, characterisation (observed green before the guard landed). The guard
/// around the <c>Uri</c> reads must change nothing for an address the parser can read.
/// </summary>
public sealed class RtspPathUnreadableAddressCharacterisationTests
{
    [Theory]
    [InlineData("rtsp://camera-sim:8554/station-4-roughing", "station-4-roughing")]
    [InlineData("rtsp://cam.example/hand/made", "hand/made")]
    public void A_readable_address_extracts_the_same_path_as_before(string url, string expected)
    {
        var extracted = RtspPath.TryExtract(url, out var path);

        extracted.ShouldBeTrue();
        path.ShouldBe(expected);
    }

    [Fact]
    public void An_address_without_a_path_is_not_a_simulated_camera_path()
    {
        var extracted = RtspPath.TryExtract("rtsp://cam", out var path);

        extracted.ShouldBeFalse();
        path.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task An_address_without_a_path_logs_the_skip_line_and_makes_no_camera_sim_request()
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "No path", "rtsp://cam");

        rig.CameraSim.Requests.ShouldBeEmpty();
        rig.Log.Entries
            .Select(entry => entry.Message)
            .ShouldBe([$"Camera {camera} at rtsp://cam has no path component; not a simulated camera, skipping."]);
    }
}
