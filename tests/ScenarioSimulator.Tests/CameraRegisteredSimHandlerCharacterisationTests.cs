using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976, characterisation (observed green before the log redaction landed). The
/// redaction changed what the handler logs and nothing else, so what it records and what it asks
/// of the camera-sim and the layout service must be exactly what it was. A non-asset camera is
/// still recorded against its path, and a wall attempt happens only for a complete wall of the scenario.
/// </summary>
public sealed class CameraRegisteredSimHandlerCharacterisationTests
{
    private const string Scenario = "mill";
    private const string OtherScenario = "other";
    private const string AssetPath = "station-4-roughing";

    [Fact]
    public async Task A_non_asset_camera_is_recorded_against_its_path_and_attempts_no_wall()
    {
        SimHandlerRig rig = new(WallScenario());
        var overlay = Guid.CreateVersion7();
        var camera = Guid.CreateVersion7();
        rig.Correlation.RecordOverlay(OtherScenario, "hand/made", overlay, 1, 1);

        await rig.HandleAsync(camera, "Hand Made", "rtsp://cam.example/hand/made");

        var tiles = rig.Correlation.CompleteTiles(OtherScenario);
        tiles.Count.ShouldBe(1);
        tiles[0].ShouldBe(new CorrelatedTile(camera, overlay, 1, 1));
        rig.Layouts.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scenario_asset_camera_is_recorded_against_its_asset_and_attempts_the_complete_wall()
    {
        SimHandlerRig rig = new(WallScenario());
        var overlay = Guid.CreateVersion7();
        var camera = Guid.CreateVersion7();
        rig.Correlation.RecordOverlay(Scenario, AssetPath, overlay, 0, 1);

        await rig.HandleAsync(camera, "Station 4", $"rtsp://camera-sim:8554/{AssetPath}");

        var tiles = rig.Correlation.CompleteTiles(Scenario);
        tiles.Count.ShouldBe(1);
        tiles[0].ShouldBe(new CorrelatedTile(camera, overlay, 0, 1));
        rig.Layouts.Requests.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_scenario_asset_camera_whose_path_camera_sim_already_has_logs_the_replace_text_as_today()
    {
        SimHandlerRig rig = new(WallScenario(), cameraSimAlreadyHasThePath: true);

        await rig.HandleAsync(Guid.CreateVersion7(), "Station 4", $"rtsp://camera-sim:8554/{AssetPath}");

        rig.CameraSim.Requests[^1].Path.ShouldBe($"/v3/config/paths/replace/{AssetPath}");
        rig.Log.Entries
            .Select(entry => entry.Message)
            .ShouldContain($"Replaced camera-sim path '{AssetPath}'; it now plays 'roughing.mp4'.");
    }

    private static ScenarioOptions WallScenario() =>
        new()
        {
            Active = [Scenario],
            Scenarios =
            {
                [Scenario] = new ScenarioDefinition
                {
                    Wall = new WallDefinition { Name = "Mill wall", Rows = 2, Cols = 2 },
                    Assets =
                    [
                        new AssetDefinition
                        {
                            Key = AssetPath,
                            Camera = new CameraDefinition { Path = AssetPath, Clip = "roughing.mp4" },
                            Overlay = new OverlayDefinition { Label = "Station 4" },
                            Tile = new TileDefinition { Row = 0, Col = 1 },
                        },
                    ],
                },
            },
        };
}
