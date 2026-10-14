using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Characterisation of what the seeder does for one asset (spec 364, #2933): the overlay, the
/// legacy highlight rule, the declared reactions and the camera, and which of them a refusal
/// or an absent field costs. Written green against the unrefactored seeder; the refactor must
/// leave every assertion here untouched.
/// </summary>
public sealed class ScenarioSeederAssetCharacterisationTests
{
    [Fact]
    public async Task An_asset_without_an_overlay_still_seeds_its_SetVariableValue_reaction_and_refuses_its_HighlightOverlay_one()
    {
        SeederRig rig = new();
        var asset = Asset("station-4");
        asset.Overlay = null;
        asset.Tile = null;
        asset.Variables = [new VariableDefinition { Name = "zone_state", Type = "String" }];
        asset.Reactions = [SetVariable("zone-state", "zone_state"), Highlight("flash")];

        await rig.RunAsync(OneAsset(asset));

        rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-zone-state"]);
        rig.RuleCreates[0].Property("actionType").ShouldBe("SetVariableValue");
        rig.Overlays.Requests.ShouldBeEmpty("an asset with no overlay seeds none");
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message == "Asset 'station-4' reaction 'flash' was not seeded: HighlightOverlay reaction on an asset with no seeded overlay..");
        rig.RegisteredCameras.ShouldBe(["station-4"]);
        rig.Correlation.CompleteTiles("rolling-mill").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_asset_with_an_overlay_but_no_highlight_logs_the_missing_field_and_still_seeds_its_reactions()
    {
        SeederRig rig = new();
        var asset = Asset("station-4");
        asset.Highlight = null;
        asset.Reactions = [Highlight("flash")];

        await rig.RunAsync(OneAsset(asset));

        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message == "Asset 'station-4' is missing its highlight; cannot seed it.");
        rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-flash"]);
        rig.Rules.Requests.ShouldContain(request => request.PathAndQuery == "/rules/rolling-mill-station-4-flash/publish?fabId=munich");
        rig.RegisteredCameras.ShouldBe(["station-4"]);
    }

    [Fact]
    public async Task A_highlight_rule_the_server_refuses_is_logged_and_the_reactions_still_seed()
    {
        SeederRig rig = new(rules: request =>
            request is { Method: "POST", Path: "/rules" } && request.Property("name")!.EndsWith("-highlight", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"title":"RULE_INVALID_INPUT","detail":"predicate does not parse as AEL.","status":400}""",
                        Encoding.UTF8,
                        "application/json"),
                }
                : null);
        var asset = Asset("station-4");
        asset.Highlight = TemperatureHighlight("gte");
        asset.Sensors = [TemperatureSensor()];
        asset.Reactions = [Highlight("flash")];

        await rig.RunAsync(OneAsset(asset));

        rig.RuleCreates.Select(create => create.Property("name")).ShouldBe([
            "rolling-mill-station-4-highlight",
            "rolling-mill-station-4-flash",
        ]);
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message == "Asset 'station-4' highlight rule was not seeded: predicate does not parse as AEL..");
        var publishes = rig.Rules.Requests.Where(request => request.Path.EndsWith("/publish", StringComparison.Ordinal)).ToList();
        publishes.Select(publish => publish.Path).ShouldBe(["/rules/rolling-mill-station-4-flash/publish"]);
        rig.RegisteredCameras.ShouldBe(["station-4"]);
        rig.Log.Entries.ShouldNotContain(entry => entry.Message.Contains("INCOMPLETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_highlight_with_an_unrecognised_comparison_is_refused_by_name_and_the_reactions_still_seed()
    {
        SeederRig rig = new();
        var asset = Asset("station-4");
        asset.Highlight = TemperatureHighlight("approx");
        asset.Sensors = [TemperatureSensor()];
        asset.Reactions = [Highlight("flash")];

        await rig.RunAsync(OneAsset(asset));

        rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-flash"]);
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message == "Asset 'station-4' highlight rule was not seeded: comparison 'approx' is not a recognised operator.");
        rig.RegisteredCameras.ShouldBe(["station-4"]);
    }

    [Fact]
    public async Task The_overlay_is_recorded_with_its_scenario_and_tile()
    {
        SeederRig rig = new();
        var asset = Asset("station-4");
        asset.Tile = new TileDefinition { Row = 1, Col = 0 };

        await rig.RunAsync(OneAsset(asset));

        var overlayCreate = rig.Overlays.Requests.Where(request => request is { Method: "POST", Path: "/overlays" }).ShouldHaveSingleItem();
        overlayCreate.Property("name").ShouldBe("rolling-mill-station-4");
        var tile = rig.Correlation.CompleteTiles("rolling-mill").ShouldHaveSingleItem();
        tile.Overlay.ShouldBe(SeederRig.Overlay);
        tile.Camera.ShouldBe(SeederRig.CameraIdentifier("station-4"));
        (tile.Row, tile.Col).ShouldBe((1, 0));
        rig.Correlation.CompleteTiles("some-other-plant").ShouldBeEmpty();
    }

    [Fact]
    public async Task Variables_overlay_and_rules_are_all_seeded_before_the_camera_is_registered()
    {
        SeederRig rig = new();
        var asset = Asset("station-4");
        asset.Variables = [new VariableDefinition { Name = "zone_state", Type = "String" }];
        asset.Highlight = TemperatureHighlight("gte");
        asset.Sensors = [TemperatureSensor()];
        asset.Reactions = [Highlight("flash"), SetVariable("zone-state", "zone_state")];

        await rig.RunAsync(OneAsset(asset));

        rig.Variables.Requests.ShouldHaveSingleItem();
        rig.RuleCreates.Select(create => create.Property("name")).ShouldBe([
            "rolling-mill-station-4-highlight",
            "rolling-mill-station-4-flash",
            "rolling-mill-station-4-zone-state",
        ]);
        rig.RegisteredCameras.ShouldBe(["station-4"]);
    }

    [Fact]
    public async Task A_reaction_a_sensor_satisfies_is_seeded_even_when_the_clips_sidecar_is_refused()
    {
        var clipsDirectory = CreateTempClipsDirectory();
        try
        {
            // Refused: AtMs (25000) is past the declared 20000ms duration.
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "station-4.cues.json"),
                """{ "Clip": "station-4.mp4", "DurationMs": 20000, "Cues": [ { "AtMs": 25000, "Class": "person", "Confidence": 0.9 } ] }""");
            SeederRig rig = new();
            var asset = Asset("station-4");
            asset.Sensors = [new SensorDefinition { Kind = "ObjectDetected", Unit = "count", Behaviour = "steady", Source = "inference", Mean = 1 }];
            asset.Reactions = [Highlight("flash")];

            await rig.RunAsync(OneAsset(asset, clipsDirectory));

            rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-flash"]);
            rig.Log.Entries.ShouldNotContain(entry => entry.Message.Contains("was skipped", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_reaction_a_valid_cue_satisfies_is_seeded()
    {
        var clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "station-4.cues.json"),
                """{ "Clip": "station-4.mp4", "DurationMs": 20000, "Cues": [ { "AtMs": 1000, "Class": "person", "Confidence": 0.9 } ] }""");
            SeederRig rig = new();
            var asset = Asset("station-4");
            asset.Reactions = [Highlight("flash")];

            await rig.RunAsync(OneAsset(asset, clipsDirectory));

            rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-flash"]);
            rig.Log.Entries.ShouldNotContain(
                entry => entry.Level == LogLevel.Warning && entry.Message.Contains("flash", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_reaction_only_a_cue_could_satisfy_is_skipped_when_the_sidecar_is_refused_while_a_sensor_backed_one_still_seeds()
    {
        var clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "station-4.cues.json"),
                """{ "Clip": "station-4.mp4", "DurationMs": 20000, "Cues": [ { "AtMs": 25000, "Class": "person", "Confidence": 0.9 } ] }""");
            SeederRig rig = new();
            var asset = Asset("station-4");
            asset.Sensors = [TemperatureSensor()];
            asset.Reactions =
            [
                Highlight("cue-only"),
                new ReactionDefinition
                {
                    Name = "sensor-backed",
                    When = new ReactionTrigger { Source = "plc", Kind = "Temperature", Predicate = "$.payload.value >= 1" },
                    Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
                },
            ];

            await rig.RunAsync(OneAsset(asset, clipsDirectory));

            rig.RuleCreates.Select(create => create.Property("name")).ShouldBe(["rolling-mill-station-4-sensor-backed"]);
            rig.Log.Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message == "Asset 'station-4' reaction 'cue-only' was skipped: its trigger 'inference'/'ObjectDetected' is satisfied only by a cue, and this clip's sidecar was refused.");
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    private static ReactionDefinition Highlight(string name) => new()
    {
        Name = name,
        When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
        Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
    };

    private static ReactionDefinition SetVariable(string name, string variable) => new()
    {
        Name = name,
        When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
        Then = new ReactionAction { Type = "SetVariableValue", Variable = variable, Value = "$.payload.class" },
    };

    private static HighlightDefinition TemperatureHighlight(string comparison) => new()
    {
        TriggerKind = "Temperature",
        Comparison = comparison,
        Threshold = 1_100,
        DurationMs = 4_000,
    };

    private static SensorDefinition TemperatureSensor() =>
        new() { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 };

    private static ScenarioOptions OneAsset(AssetDefinition asset, string clipsDirectory = "") => new()
    {
        Active = ["rolling-mill"],
        ClipsDirectory = clipsDirectory,
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            ["rolling-mill"] = new() { Name = "Rolling Mill", Assets = [asset] },
        },
    };

    private static AssetDefinition Asset(string key) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = $"{key}.mp4" },
        Overlay = new OverlayDefinition { Label = key, X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18, FontSize = 24 },
        Tile = new TileDefinition { Row = 0, Col = 0 },
    };

    private static string CreateTempClipsDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sse-asset-char-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        return directory;
    }
}
