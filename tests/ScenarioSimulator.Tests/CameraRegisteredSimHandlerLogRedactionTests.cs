using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976. A camera registered by hand on a dev stack arrives with its operator-typed
/// URL; the simulator may log its identifier and origin, never the URL, its userinfo, query or
/// path. Every assertion reads every record (message, structured values, exception text), so a
/// secret moved to another field or line still fails.
/// </summary>
public sealed class CameraRegisteredSimHandlerLogRedactionTests
{
    [Fact]
    public async Task A_camera_with_no_url_path_is_skipped_by_identifier_and_origin()
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Hand Made", "rtsp://admin:hunter2@cam.example:8554?token=SECRET");

        rig.CameraSim.Requests.ShouldBeEmpty();

        var skipped = rig.Log.Entries
            .Where(entry => entry.Message.Contains("not a simulated camera", StringComparison.Ordinal))
            .ToList();
        skipped.Count.ShouldBe(1, rig.Log.Dump);
        skipped[0].Text.ShouldContain(camera.ToString(), customMessage: rig.Log.Dump);
        skipped[0].Text.ShouldContain("rtsp://cam.example:8554", customMessage: rig.Log.Dump);

        foreach (var secret in new[] { "hunter2", "admin", "token", "SECRET" })
        {
            rig.Log.Dump.ShouldNotContain(secret, customMessage: $"'{secret}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Theory]
    [InlineData(false, "add")]
    [InlineData(true, "replace")]
    public async Task A_non_asset_camera_never_logs_its_url_path(bool cameraSimAlreadyHasThePath, string verb)
    {
        SimHandlerRig rig = new(cameraSimAlreadyHasThePath: cameraSimAlreadyHasThePath);
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Line 7 Spare", "rtsp://admin:pw@cam.example/SECRET-token/stream");

        // Behaviour that must not move: the path is still the camera-sim path name.
        rig.CameraSim.Requests[^1].Path.ShouldBe($"/v3/config/paths/{verb}/SECRET-token/stream");

        rig.Log.Dump.ShouldContain(camera.ToString());
        rig.Log.Dump.ShouldContain("rtsp://cam.example");
        rig.Log.Dump.ShouldContain("Line 7 Spare");

        foreach (var secret in new[] { "SECRET-token", ":pw", "admin" })
        {
            rig.Log.Dump.ShouldNotContain(secret, customMessage: $"'{secret}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Fact]
    public async Task A_scenario_asset_camera_logs_its_asset_path_as_today()
    {
        SimHandlerRig rig = new(AssetScenario("station-4-roughing", "roughing.mp4"));
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Station 4", "rtsp://camera-sim:8554/station-4-roughing");

        var messages = rig.Log.Entries.Select(entry => entry.Message).ToList();
        messages.ShouldContain($"Recorded camera {camera} for asset 'station-4-roughing'.");
        messages.ShouldContain("Provisioned camera-sim loop path 'station-4-roughing' playing 'roughing.mp4'.");
    }

    [Fact]
    public async Task An_unparseable_url_is_skipped_without_being_printed()
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Hand Made", "not a url :// hunter2");

        rig.CameraSim.Requests.ShouldBeEmpty();
        rig.Log.Dump.ShouldContain(camera.ToString());
        rig.Log.Dump.ShouldContain("<unparseable url>");
        rig.Log.Dump.ShouldNotContain("hunter2", customMessage: rig.Log.Dump);
    }

    private static ScenarioOptions AssetScenario(string path, string clip) =>
        new()
        {
            Active = ["mill"],
            Scenarios =
            {
                ["mill"] = new ScenarioDefinition
                {
                    Assets = [new AssetDefinition { Key = "station-4", Camera = new CameraDefinition { Path = path, Clip = clip } }],
                },
            },
        };
}
