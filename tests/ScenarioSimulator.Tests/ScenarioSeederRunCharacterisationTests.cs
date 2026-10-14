using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Characterisation of <c>ScenarioSeeder.ExecuteAsync</c>'s whole-run behaviour (spec 364, #2933):
/// which scenarios are seeded, what each failure costs, and how the failures add up in the
/// closing summary. Written green against the unrefactored seeder; the refactor must leave
/// every assertion here untouched.
/// </summary>
public sealed class ScenarioSeederRunCharacterisationTests
{
    [Fact]
    public async Task An_active_key_missing_from_configuration_is_logged_and_the_next_scenario_still_seeds()
    {
        SeederRig rig = new();
        ScenarioOptions options = new()
        {
            Active = ["no-such-plant", "rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4")),
            },
        };

        await rig.RunAsync(options);

        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message == "Active scenario 'no-such-plant' not found in configuration; nothing to seed.");
        rig.RegisteredCameras.ShouldBe(["station-4"]);
        rig.Log.Entries.ShouldContain(entry => entry.Message == "Scenario 'Rolling Mill' seeded.");
        rig.Log.Entries.Count(entry => entry.Message.StartsWith("Seeding scenario ", StringComparison.Ordinal))
            .ShouldBe(1, "the missing key is skipped, not seeded as an empty scenario");
        rig.Log.Entries.ShouldNotContain(entry => entry.Message.Contains("INCOMPLETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scenario_whose_manifests_cannot_load_is_counted_once_logs_no_seeded_line_and_the_next_scenario_still_seeds()
    {
        SeederRig rig = new();
        var unloadable = Asset("bad-station");
        unloadable.Camera.Clip = null!;
        ScenarioOptions options = new()
        {
            Active = ["bad-plant", "rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["bad-plant"] = Scenario("Bad Plant", unloadable, Asset("other-bad-station")),
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4")),
            },
        };

        await rig.RunAsync(options);

        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.StartsWith("Scenario 'bad-plant' manifest loading failed: ", StringComparison.Ordinal));
        rig.Log.Entries.ShouldContain(entry => entry.Message == "Seeding scenario 'Bad Plant' with 2 asset(s).");
        rig.Log.Entries.ShouldNotContain(entry => entry.Message == "Scenario 'Bad Plant' seeded.");
        rig.RegisteredCameras.ShouldBe(["station-4"], "no asset of the unloadable scenario is attempted");
        rig.Log.Entries.ShouldContain(entry => entry.Message == "Scenario 'Rolling Mill' seeded.");
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.StartsWith("Seeding finished INCOMPLETE: 1 step(s) failed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_registered_cameras_identifier_is_recorded_for_its_wall_tile()
    {
        SeederRig rig = new();
        ScenarioOptions options = new()
        {
            Active = ["rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4")),
            },
        };

        await rig.RunAsync(options);

        rig.Correlation.IsWallComplete("rolling-mill", 1, out var ready).ShouldBeTrue();
        ready.ShouldBe(1);
        var tile = rig.Correlation.CompleteTiles("rolling-mill").ShouldHaveSingleItem();
        tile.Camera.ShouldBe(SeederRig.CameraIdentifier("station-4"));
    }

    [Fact]
    public async Task A_camera_whose_identifier_is_unknown_is_not_recorded()
    {
        SeederRig rig = new(cameras: request => request.Method switch
        {
            "POST" => new HttpResponseMessage(HttpStatusCode.Conflict),
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
        });
        ScenarioOptions options = new()
        {
            Active = ["rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4")),
            },
        };

        await rig.RunAsync(options);

        rig.Correlation.IsWallComplete("rolling-mill", 1, out var ready).ShouldBeFalse();
        ready.ShouldBe(0);
        rig.Correlation.CompleteTiles("rolling-mill").ShouldBeEmpty();
        rig.Log.Entries.ShouldContain(entry => entry.Message == "Scenario 'Rolling Mill' seeded.");
        rig.Log.Entries.ShouldNotContain(
            entry => entry.Message.Contains("INCOMPLETE", StringComparison.Ordinal),
            "an unreadable identifier is not a failed step");
    }

    [Fact]
    public async Task The_incomplete_summary_counts_asset_and_wall_failures_together()
    {
        SeederRig rig = new(
            cameras: _ => throw new HttpRequestException("catalog unreachable"),
            wallOptions: new ThrowingOptions());
        ScenarioOptions options = new()
        {
            Active = ["rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4"), Asset("station-7")),
            },
        };

        await rig.RunAsync(options);

        rig.Log.Entries.Count(entry => entry.Message.Contains("could not be seeded", StringComparison.Ordinal))
            .ShouldBe(2);
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.StartsWith("Wall seeding failed: wall options unavailable.", StringComparison.Ordinal));
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.StartsWith("Seeding finished INCOMPLETE: 3 step(s) failed.", StringComparison.Ordinal));
        rig.Log.Entries.Count(entry => entry.Message.Contains("INCOMPLETE", StringComparison.Ordinal))
            .ShouldBe(1, "the summary is said once, at the end");
    }

    [Fact]
    public async Task A_failing_asset_does_not_stop_the_scenarios_later_assets_and_the_scenario_is_still_reported_seeded()
    {
        SeederRig rig = new(cameras: request =>
            request.Property("name") == "station-4" ? throw new HttpRequestException("catalog unreachable") : null);
        ScenarioOptions options = new()
        {
            Active = ["rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = Scenario("Rolling Mill", Asset("station-4"), Asset("station-7")),
            },
        };

        await rig.RunAsync(options);

        rig.RegisteredCameras.ShouldBe(["station-4", "station-7"]);
        rig.Log.Entries.ShouldContain(entry => entry.Message == "Scenario 'Rolling Mill' seeded.");
        rig.Log.Entries.ShouldContain(entry =>
            entry.Message.StartsWith("Asset 'rolling-mill'/'station-4' could not be seeded: ", StringComparison.Ordinal));
        rig.Log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.StartsWith("Seeding finished INCOMPLETE: 1 step(s) failed.", StringComparison.Ordinal));
    }

    private static ScenarioDefinition Scenario(string name, params AssetDefinition[] assets) =>
        new() { Name = name, Assets = [.. assets] };

    private static AssetDefinition Asset(string key) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = $"{key}.mp4" },
        Overlay = new OverlayDefinition { Label = key, X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18, FontSize = 24 },
        Tile = new TileDefinition { Row = 0, Col = 0 },
    };

    private sealed class ThrowingOptions : IOptions<ScenarioOptions>
    {
        public ScenarioOptions Value => throw new InvalidOperationException("wall options unavailable.");
    }
}
