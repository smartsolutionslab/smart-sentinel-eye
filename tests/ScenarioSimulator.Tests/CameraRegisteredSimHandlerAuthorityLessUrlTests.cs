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
    [InlineData("rtsp://[::1]../../../../SECRET-token", false)]
    [InlineData("rtsp://[::1]../../../../SECRET-token", true)]
    [InlineData("rtsp://[::1]%2E%2E/%2E%2E/SECRET", false)]
    [InlineData("rtsp://[::1]%2E%2E/%2E%2E/SECRET", true)]
    [InlineData("rtsp://[::1]../replace/x", false)]
    [InlineData("rtsp://[::1]../replace/x", true)]
    [InlineData("rtsp://[::1]../::1//:x.SECRET", false)]
    [InlineData("rtsp://[::1]../::1//:x.SECRET", true)]
    [InlineData("rtsp://[::1]..//SECRET", false)]
    [InlineData("rtsp://[::1]..//SECRET", true)]
    [InlineData("rtsp://[::1]../add/x", false)]
    [InlineData("rtsp://[::1]../add/x", true)]
    [InlineData("rtsp://[::1]%2E%2E/add/x", false)]
    [InlineData("rtsp://[::1]%2E%2E/add/x", true)]
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

    [Theory]
    [InlineData("rtsp://h/a:b/c", "a:b/c")]
    [InlineData("rtsp://h/ch1:main", "ch1:main")]
    [InlineData("rtsp://h/c:/x", "c:/x")]
    [InlineData("rtsp://h/evil.com:x", "evil.com:x")]
    [InlineData("rtsp://h/http://evil.com/x", "http://evil.com/x")]
    public async Task A_colon_in_the_first_path_segment_is_not_a_scheme_and_is_provisioned_under_the_add_prefix(
        string url,
        string expectedPath)
    {
        SimHandlerRig rig = new();

        await rig.HandleAsync(Guid.CreateVersion7(), "Station 4", url);

        rig.CameraSim.Requests
            .Select(request => $"{request.Method} {request.Path}")
            .ShouldBe([$"POST /v3/config/paths/add/{expectedPath}"]);
    }
}
