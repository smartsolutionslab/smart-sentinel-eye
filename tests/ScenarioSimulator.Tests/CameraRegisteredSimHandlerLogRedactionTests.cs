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
    [InlineData(false, "add", "Added")]
    [InlineData(true, "replace", "Replaced")]
    public async Task A_non_asset_camera_never_logs_its_url_path(bool cameraSimAlreadyHasThePath, string verb, string write)
    {
        SimHandlerRig rig = new(cameraSimAlreadyHasThePath: cameraSimAlreadyHasThePath);
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Line 7 Spare", "rtsp://admin:pw@cam.example/SECRET-token/stream");

        // Behaviour that must not move: the path is still the camera-sim path name.
        rig.CameraSim.Requests[^1].Path.ShouldBe($"/v3/config/paths/{verb}/SECRET-token/stream");

        rig.Log.Dump.ShouldContain(camera.ToString());
        rig.Log.Dump.ShouldContain("rtsp://cam.example");

        // The labelled line is still written: it names the camera and how the path was written.
        var labelled = rig.Log.Entries
            .Where(entry => entry.Message.Contains("now carries the label of camera", StringComparison.Ordinal))
            .ToList();
        labelled.Count.ShouldBe(1, rig.Log.Dump);
        labelled[0].Message.ShouldContain(camera.ToString(), customMessage: rig.Log.Dump);
        labelled[0].Message.ShouldContain($"was {write} and", customMessage: rig.Log.Dump);

        rig.Log.Dump.ShouldNotContain("Line 7 Spare", customMessage: rig.Log.Dump);

        // The label still reaches the picture: it is the camera-sim request body, not a log line.
        rig.CameraSim.Requests[^1].Body.ShouldContain("Line 7 Spare");

        foreach (var secret in new[] { "SECRET-token", ":pw", "admin" })
        {
            rig.Log.Dump.ShouldNotContain(secret, customMessage: $"'{secret}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Fact]
    public async Task A_non_asset_camera_name_with_line_breaks_cannot_forge_a_line()
    {
        SimHandlerRig rig = new();

        await rig.HandleAsync(Guid.CreateVersion7(), "Spare\r\nERROR forged line", "rtsp://cam.example/SECRET-token/stream");

        rig.CameraSim.Requests.ShouldNotBeEmpty();
        rig.Log.Entries.ShouldNotBeEmpty();
        rig.Log.Dump.ShouldNotContain("forged line", customMessage: rig.Log.Dump);

        // Per entry: the joined Dump carries the platform line separator between entries.
        foreach (var entry in rig.Log.Entries)
        {
            entry.Text.ShouldNotContain("\r", customMessage: rig.Log.Dump);
            entry.Text.ShouldNotContain("\n", customMessage: rig.Log.Dump);
        }
    }

    [Fact]
    public async Task A_non_asset_camera_name_that_is_a_url_is_not_logged()
    {
        SimHandlerRig rig = new();

        await rig.HandleAsync(Guid.CreateVersion7(), "rtsp://admin:hunter2@cam.example/x", "rtsp://cam.example/SECRET-token/stream");

        rig.CameraSim.Requests.ShouldNotBeEmpty();
        rig.Log.Entries.ShouldNotBeEmpty();
        rig.Log.Dump.ShouldNotContain("hunter2", customMessage: rig.Log.Dump);
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
