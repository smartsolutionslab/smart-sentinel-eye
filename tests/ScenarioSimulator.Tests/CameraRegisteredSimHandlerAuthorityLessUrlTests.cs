using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 406 / #3025. An authority-less camera URL keeps its dot segments, so provisioning its path
/// would let the request climb out of <c>/v3/config/paths/&lt;verb&gt;/</c> and defeat the span
/// redaction. The handler must drop it through its existing skip line instead, in both the add and
/// the replace branch.
/// </summary>
public sealed class CameraRegisteredSimHandlerAuthorityLessUrlTests
{
    [Theory]
    [InlineData("rtsp:../../../../SECRET-token", false)]
    [InlineData("rtsp:../../../../SECRET-token", true)]
    [InlineData("rtsp:../SECRET/x", false)]
    [InlineData("rtsp:../SECRET/x", true)]
    public async Task An_authority_less_url_makes_no_camera_sim_request_and_is_skipped(
        string url,
        bool cameraSimAlreadyHasThePath)
    {
        SimHandlerRig rig = new(cameraSimAlreadyHasThePath: cameraSimAlreadyHasThePath);
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Forged", url);

        rig.CameraSim.Requests
            .Select(request => $"{request.Method} {request.Path}")
            .ShouldBeEmpty();
        rig.Log.Entries
            .Select(entry => entry.Message)
            .ShouldBe([$"Camera {camera} at <unparseable url> has no path component; not a simulated camera, skipping."]);
        rig.Log.Dump.ShouldNotContain("SECRET");
    }

    [Fact]
    public async Task A_url_with_a_host_is_provisioned_under_the_add_prefix_as_today()
    {
        SimHandlerRig rig = new();

        await rig.HandleAsync(Guid.CreateVersion7(), "Station 4", "rtsp://camera-sim:8554/station-4-roughing");

        rig.CameraSim.Requests
            .Select(request => $"{request.Method} {request.Path}")
            .ShouldBe(["POST /v3/config/paths/add/station-4-roughing"]);
    }
}
