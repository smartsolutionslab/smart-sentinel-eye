using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976, adversarial pass (T011). URL shapes the phase-1 tests do not send, read over
/// every record the handler and the provisioner write. Every marker is a token no template contains,
/// so a hit can only be the URL leaking.
/// </summary>
public sealed class CameraRegisteredSimHandlerLogRedactionAdversarialTests
{
    public static TheoryData<string, string[]> HostileUrls => new()
    {
        { "rtsp://USERMARK:PWMARK@", ["USERMARK", "PWMARK"] },
        { @"rtsp:\\USERMARK:PWMARK#@cam.example/x", ["USERMARK", "PWMARK"] },
        { "rtsp://cam.example/%53ECRETMARK/stream", ["ECRETMARK"] },
        { "rtsp://USERMARK:PWMARK@[fe80::1%25eth0]:8554/PATHMARK", ["USERMARK", "PWMARK", "PATHMARK", "eth0"] },
        { "rtsp://cam.example/stream?token=QUERYMARK", ["QUERYMARK"] },
        { "rtsp://cam.example/PATHMARK#FRAGMARK", ["PATHMARK", "FRAGMARK"] },
        { $"rtsp://cam.example/{new string('a', 8000)}LONGMARK", ["LONGMARK", new string('a', 64)] },
        { "rtsp://cam.example/CTRL%0D%0APATHMARK", ["PATHMARK"] },
        { "rtsp://cam.example/CTRL\u0007PATHMARK", ["PATHMARK"] },
        { "rtsp://cam.example/ŠECRETMARK/stream", ["ECRETMARK"] },
        { "rtsp://USERMARK@cam.example/PATHMARK", ["USERMARK", "PATHMARK"] },
        { "rtsp://USERMARK:PWMARK@cam.example:8554/rtsp://USERMARK:PWMARK@inner.example/PATHMARK", ["USERMARK", "PWMARK", "PATHMARK", "inner.example"] },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task A_camera_registered_with_no_url_is_skipped_by_identifier_without_throwing(string? url)
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await Should.NotThrowAsync(() => rig.HandleAsync(camera, "Hand Made", url!));

        rig.CameraSim.Requests.ShouldBeEmpty();

        var entry = rig.Log.Entries.ShouldHaveSingleItem(rig.Log.Dump);
        entry.Message.ShouldBe(
            $"Camera {camera} at {UrlRedaction.Unparseable} has no path component; not a simulated camera, skipping.",
            rig.Log.Dump);
        entry.Exception.ShouldBeEmpty(rig.Log.Dump);
    }

    [Theory]
    [MemberData(nameof(HostileUrls))]
    public async Task A_hostile_url_shape_never_reaches_any_log_record(string url, string[] markers)
    {
        SimHandlerRig rig = new();
        var camera = Guid.CreateVersion7();

        await Should.NotThrowAsync(() => rig.HandleAsync(camera, "Hand Made", url));

        rig.Log.Dump.ShouldContain(camera.ToString(), customMessage: rig.Log.Dump);

        foreach (var marker in markers)
        {
            rig.Log.Dump.ShouldNotContain(marker, customMessage: $"'{marker}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Theory]
    [MemberData(nameof(HostileUrls))]
    public async Task A_hostile_url_shape_never_reaches_any_log_record_on_the_replace_branch(string url, string[] markers)
    {
        SimHandlerRig rig = new(cameraSimAlreadyHasThePath: true);
        var camera = Guid.CreateVersion7();

        await Should.NotThrowAsync(() => rig.HandleAsync(camera, "Hand Made", url));

        foreach (var marker in markers)
        {
            rig.Log.Dump.ShouldNotContain(marker, customMessage: $"'{marker}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Fact]
    public async Task A_hostile_url_whose_path_names_an_asset_logs_only_the_configured_asset_path()
    {
        SimHandlerRig rig = new(AssetScenario("station-4-roughing", "roughing.mp4"));
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(
            camera,
            "Station 4",
            "rtsp://USERMARK:PWMARK@evil.example:8554/station-4-roughing?token=QUERYMARK#FRAGMARK");

        rig.CameraSim.Requests.ShouldHaveSingleItem().Path.ShouldBe("/v3/config/paths/add/station-4-roughing");

        var messages = rig.Log.Entries.Select(entry => entry.Message).ToList();
        messages.ShouldContain($"Recorded camera {camera} for asset 'station-4-roughing'.", rig.Log.Dump);
        messages.ShouldContain("Provisioned camera-sim loop path 'station-4-roughing' playing 'roughing.mp4'.", rig.Log.Dump);

        foreach (var marker in new[] { "USERMARK", "PWMARK", "QUERYMARK", "FRAGMARK", "evil.example" })
        {
            rig.Log.Dump.ShouldNotContain(marker, customMessage: $"'{marker}' leaked:{Environment.NewLine}{rig.Log.Dump}");
        }
    }

    [Fact]
    public async Task An_asset_path_that_differs_only_in_case_is_not_an_asset_and_its_path_is_not_logged()
    {
        SimHandlerRig rig = new(AssetScenario("station-4-roughing", "roughing.mp4"));
        var camera = Guid.CreateVersion7();

        await rig.HandleAsync(camera, "Station 4", "rtsp://cam.example/STATION-4-ROUGHING-PATHMARK");

        rig.Log.Entries.Select(entry => entry.Message).ShouldNotContain(
            message => message.Contains("for asset", StringComparison.Ordinal),
            rig.Log.Dump);
        rig.Log.Dump.ShouldNotContain("PATHMARK", customMessage: rig.Log.Dump);
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
