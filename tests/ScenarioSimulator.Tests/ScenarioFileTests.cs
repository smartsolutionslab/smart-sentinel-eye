using Microsoft.Extensions.Configuration;
using Shouldly;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 044 — reads the real <c>Scenarios/*.json</c> and the real clips
/// directory, because the failures this feature is about are failures of
/// *content*, not of code. A scenario whose four assets name one clip compiles,
/// binds, provisions and shows a wall of four identical tiles.
/// </summary>
public sealed class ScenarioFileTests
{
    [Theory]
    [InlineData("rolling-mill")]
    [InlineData("paper-mill")]
    [InlineData("electronics")]
    public void Every_asset_in_a_scenario_plays_a_different_clip(string key)
    {
        ScenarioDefinition scenario = Load().Scenarios[key];

        string[] clips = scenario.Assets.Select(asset => asset.Camera.Clip).ToArray();

        clips.Length.ShouldBeGreaterThan(0);
        clips.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(clips.Length);
    }

    /// <summary>
    /// FR-007's other half: the check catches a clip that is absent, so this
    /// catches a scenario that names one. Between them a tile cannot silently
    /// fail to go live.
    /// </summary>
    [Theory]
    [InlineData("rolling-mill")]
    [InlineData("paper-mill")]
    [InlineData("electronics")]
    public void Every_clip_a_scenario_names_exists_on_disk(string key)
    {
        ClipLibrary clips = new(ClipsDirectory());

        foreach (AssetDefinition asset in Load().Scenarios[key].Assets)
        {
            clips.Exists(asset.Camera.Clip)
                .ShouldBeTrue($"{key}/{asset.Key} names {asset.Camera.Clip}, which is not in the clips directory");
        }
    }

    /// <summary>
    /// Each clip carries its licence beside it, and a missing one is invisible
    /// until somebody audits the repository. Asserted rather than trusted,
    /// because the clips arrive by script and the attribution files by hand.
    /// </summary>
    [Fact]
    public void Every_clip_in_the_repository_has_an_attribution_file()
    {
        string directory = ClipsDirectory();

        foreach (string clip in Directory.EnumerateFiles(directory, "*.mp4"))
        {
            string attribution = Path.ChangeExtension(clip, null) + ".ATTRIBUTION.txt";
            File.Exists(attribution).ShouldBeTrue($"{Path.GetFileName(clip)} has no ATTRIBUTION.txt beside it");
            File.ReadAllText(attribution).ShouldContain("creativecommons.org");
        }
    }

    /// <summary>
    /// Three plants seed; one animates. Asserting the count is what stops the
    /// single-timeline decision being quietly "fixed" into three.
    /// </summary>
    [Fact]
    public void Three_scenarios_are_active_and_exactly_one_of_them_animates()
    {
        ScenarioOptions options = Load();

        options.Active.Count.ShouldBe(3);
        options.Animated.ShouldBe(options.Active[0]);
        options.Active.ShouldAllBe(key => options.Scenarios.ContainsKey(key));
    }

    private static ScenarioOptions Load()
    {
        string scenarios = Path.Combine(AppContext.BaseDirectory, "Scenarios");

        IConfigurationBuilder builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);

        foreach (string file in Directory.EnumerateFiles(scenarios, "*.json"))
        {
            builder.AddJsonFile(file, optional: false);
        }

        ScenarioOptions options = new();
        builder.Build().GetSection(ScenarioOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>
    /// Spec 289 / PR-B, T-B09. Every <c>*.cues.json</c> sidecar that actually
    /// exists in the clips directory must name a clip that is really there and
    /// pass <see cref="ClipManifestValidation"/>. No longer vacuous:
    /// <c>mill-roughing.cues.json</c> ships as real content (T-B16).
    /// </summary>
    [Fact]
    public void Every_cues_json_sidecar_names_a_clip_that_exists_and_is_itself_valid()
    {
        string directory = ClipsDirectory();

        foreach (string sidecar in Directory.EnumerateFiles(directory, "*.cues.json"))
        {
            string clip = Path.GetFileName(sidecar).Replace(".cues.json", ".mp4", StringComparison.Ordinal);

            File.Exists(Path.Combine(directory, clip))
                .ShouldBeTrue($"{Path.GetFileName(sidecar)} names a clip that is not in the clips directory");

            ClipManifestLoadResult result = ClipManifestLoader.Load(directory, clip);

            result.Violations.ShouldBeEmpty(
                $"{Path.GetFileName(sidecar)}: " + string.Join("; ", result.Violations.Select(v => v.Message)));
            result.Manifest.HasValue.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Backend-reviewer finding S5 (should-fix). <c>ClipManifest.cs</c>'s own
    /// doc comment claims <c>DurationMs</c> is "content-checked against the
    /// file, T-B09" — nothing has ever done that. Reads each shipped
    /// sidecar's clip via its <c>moov/mvhd</c> box (plan.md T-B09: "no ffprobe
    /// dependency") and asserts the declared <c>DurationMs</c> matches the
    /// file's actual duration to within 1ms, so a re-cut clip whose duration
    /// silently drifted from its sidecar is caught here rather than by a
    /// cue firing against the wrong offset.
    /// </summary>
    [Fact]
    public void Every_cues_json_sidecars_DurationMs_matches_its_clips_actual_duration_within_1ms()
    {
        string directory = ClipsDirectory();

        foreach (string sidecar in Directory.EnumerateFiles(directory, "*.cues.json"))
        {
            string clipName = Path.GetFileName(sidecar).Replace(".cues.json", ".mp4", StringComparison.Ordinal);
            string clipPath = Path.Combine(directory, clipName);
            if (!File.Exists(clipPath))
            {
                continue; // named-but-missing is already asserted by the sibling fact above
            }

            ClipManifestLoadResult result = ClipManifestLoader.Load(directory, clipName);
            if (!result.Manifest.HasValue)
            {
                continue; // an invalid sidecar is already asserted by the sibling fact above
            }

            double? actualDurationMs = Mp4Duration.ReadMs(clipPath);
            actualDurationMs.ShouldNotBeNull($"{clipName} has no mvhd box to read a duration from");

            ((double)result.Manifest.Value.DurationMs).ShouldBe(
                actualDurationMs.Value, tolerance: 1.0,
                $"{Path.GetFileName(sidecar)} declares DurationMs={result.Manifest.Value.DurationMs}, "
                + $"but {clipName}'s own mvhd box says {actualDurationMs}ms — the clip was re-cut "
                + "without its sidecar being updated");
        }
    }

    /// <summary>
    /// Walks up to the repository root: the clips live with the AppHost, not with
    /// the test binary, and the test asserts about the real ones on purpose.
    /// </summary>
    private static string ClipsDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "AppHost")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("could not locate the repository root from the test output directory");
        return Path.Combine(directory.FullName, "src", "AppHost", "Resources", "clips");
    }
}
