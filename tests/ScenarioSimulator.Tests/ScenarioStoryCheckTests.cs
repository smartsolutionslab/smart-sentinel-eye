using Shouldly;
using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-D, T-D01 (ADR-0144 red). <c>Scenario/ScenarioStoryCheck.cs</c>
/// does not exist yet — this is its target shape (plan.md §5.5, FR-013,
/// T-D03). No stub is written: plan.md and tasks.md name no shape-only stub
/// task for this PR (unlike spec 297's Badge.tsx, plan §6/T006), and PR-B's
/// own red commit for this same spec ("Round 1 red: every new type the tests
/// reference ... did not exist; tests only, no implementation") already set
/// the precedent — a missing-type compile failure, quoted verbatim, is the
/// evidence, not a licence to write the type.
///
/// <para>
/// <b>Design choice, flagged for the engineer (T-D03):</b> <c>Find</c> is
/// pure (plan.md §5.5 — "Scenario/ScenarioStoryCheck.cs (pure)"), so it takes
/// each clip's already-loaded <see cref="ClipManifestLoadResult"/> rather
/// than touching disk itself — the same dictionary <c>ScenarioSeeder</c>
/// builds once per scenario and shares with <c>SeedReactionsAsync</c> (T-D03:
/// "load each asset's manifest once ... rather than loading twice"), keyed
/// by <see cref="AssetDefinition.Camera"/>'s clip file name since two assets
/// on the same clip share its cues (spec.md "Edge Cases"). A declared
/// <see cref="ReactionDefinition"/> is named by its own <c>Name</c>; the
/// legacy <see cref="HighlightDefinition"/> has none, so it is named
/// <c>"highlight"</c> — the literal already used as the rule-name suffix
/// everywhere else (<c>HighlightRuleSeed</c>, <c>Log.HighlightRuleRefused</c>).
/// <see cref="StoryFinding.Trigger"/> is <c>"{Source}/{Kind}"</c> for a
/// reaction and bare <c>Kind</c> for the highlight, matching plan.md §5.5's
/// own asymmetry: a reaction is matched by <c>(Source, Kind)</c>, the
/// highlight — which declares no <c>Source</c> — by <c>Kind</c> alone.
/// </para>
/// </summary>
public sealed class ScenarioStoryCheckTests
{
    [Fact]
    public void An_orphan_reaction_yields_exactly_one_finding_naming_the_scenario_asset_reaction_and_trigger()
    {
        AssetDefinition asset = Asset("coiler", "coiler.mp4");
        asset.Reactions = [Reaction("person-in-exclusion", "inference", "ObjectDetected")];
        // No sensor and no cue on this asset emits inference/ObjectDetected.

        IReadOnlyList<StoryFinding> findings = ScenarioStoryCheck.Find("rolling-mill", [asset], NoManifests(asset));

        StoryFinding finding = findings.ShouldHaveSingleItem();
        finding.Scenario.ShouldBe("rolling-mill");
        finding.Asset.ShouldBe("coiler");
        finding.Reaction.ShouldBe("person-in-exclusion");
        finding.Trigger.ShouldBe("inference/ObjectDetected");
    }

    [Fact]
    public void A_reaction_whose_trigger_a_sensor_emits_yields_no_finding()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Sensors = [Sensor("Temperature", "plc")];
        asset.Reactions = [Reaction("overheat", "plc", "Temperature")];

        ScenarioStoryCheck.Find("rolling-mill", [asset], NoManifests(asset)).ShouldBeEmpty();
    }

    [Fact]
    public void A_reaction_whose_trigger_a_valid_cue_emits_yields_no_finding()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Reactions = [Reaction("billet-on-conveyor", "inference", "ObjectDetected")];

        IReadOnlyDictionary<string, ClipManifestLoadResult> manifests =
            ManifestFor(asset.Camera.Clip, Cue("inference", "ObjectDetected"));

        ScenarioStoryCheck.Find("rolling-mill", [asset], manifests).ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 289 PR-D decision (2026-10-01 gate): a refused sidecar's
    /// <see cref="ClipManifestLoadResult.Manifest"/> is <c>Option&lt;ClipManifest&gt;.None</c>,
    /// identical to "no sidecar at all" — accepted as indistinguishable today.
    /// </summary>
    [Fact]
    public void A_trigger_satisfied_only_by_a_refused_sidecars_cue_yields_a_finding()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Reactions = [Reaction("billet-on-conveyor", "inference", "ObjectDetected")];

        IReadOnlyDictionary<string, ClipManifestLoadResult> manifests = new Dictionary<string, ClipManifestLoadResult>(StringComparer.Ordinal)
        {
            [asset.Camera.Clip] = new ClipManifestLoadResult(
                Option<ClipManifest>.None, [new CueViolation(0, "AtMs 25000 is past the clip's 20000ms duration")]),
        };

        StoryFinding finding = ScenarioStoryCheck.Find("rolling-mill", [asset], manifests).ShouldHaveSingleItem();
        finding.Reaction.ShouldBe("billet-on-conveyor");
        finding.Trigger.ShouldBe("inference/ObjectDetected");
    }

    /// <summary>
    /// The legacy <see cref="HighlightDefinition"/> declares no <c>Source</c>
    /// (plan.md §5.5), so a sensor of a <em>different</em> source than the
    /// highlight would have derived still satisfies it by <c>Kind</c> alone —
    /// proving the match is not accidentally requiring a source nobody declared.
    /// </summary>
    [Fact]
    public void The_legacy_highlight_is_satisfied_by_a_sensor_of_any_source_matching_only_its_kind()
    {
        AssetDefinition asset = Asset("vision-station", "vision.mp4");
        asset.Highlight = Highlight("DefectCount");
        asset.Sensors = [Sensor("DefectCount", "camera")];

        ScenarioStoryCheck.Find("mixed-source-plant", [asset], NoManifests(asset)).ShouldBeEmpty();
    }

    [Fact]
    public void The_legacy_highlight_is_satisfied_by_a_cue_matching_only_its_kind()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Highlight = Highlight("ObjectDetected");

        IReadOnlyDictionary<string, ClipManifestLoadResult> manifests =
            ManifestFor(asset.Camera.Clip, Cue("inference", "ObjectDetected"));

        ScenarioStoryCheck.Find("rolling-mill", [asset], manifests).ShouldBeEmpty();
    }

    [Fact]
    public void A_legacy_highlight_whose_kind_no_sensor_or_cue_emits_yields_a_finding_naming_it_highlight()
    {
        AssetDefinition asset = Asset("coiler", "coiler.mp4");
        asset.Highlight = Highlight("CoilWeight");
        asset.Sensors = [Sensor("Humidity", "plc")];

        StoryFinding finding = ScenarioStoryCheck.Find("rolling-mill", [asset], NoManifests(asset)).ShouldHaveSingleItem();
        finding.Scenario.ShouldBe("rolling-mill");
        finding.Asset.ShouldBe("coiler");
        finding.Reaction.ShouldBe("highlight");
        finding.Trigger.ShouldBe("CoilWeight");
    }

    /// <summary>
    /// SC-003/FR-009: PR-A's characterisation already pins that all 12
    /// shipped highlights match a sensor and both shipped reactions match
    /// only their clip's cue. Loads the real <c>Scenarios/*.json</c> and the
    /// real clips directory (<see cref="ScenarioFileTests.ClipsDirectory"/>)
    /// — not an empty one, which the counterfactual below proves matters.
    /// </summary>
    [Fact]
    public void All_three_shipped_scenarios_yield_zero_findings_against_the_real_clips_directory()
    {
        List<StoryFinding> findings = FindingsAcrossShippedScenarios(ScenarioFileTests.ClipsDirectory());

        findings.ShouldBeEmpty(string.Join("; ", findings.Select(f => $"{f.Scenario}/{f.Asset}/{f.Reaction} ({f.Trigger})")));
    }

    /// <summary>
    /// Counterfactual for the fact above (memory: an assertion must not check
    /// its own input). With no sidecars read, the two shipped reactions
    /// (<c>billet-on-conveyor</c>, <c>defect-detected</c>) — each satisfied
    /// only by its clip's default-kind cue, never by a sensor — become
    /// unreachable. Exactly 2, per tasks.md's own statement of the correct
    /// answer for an empty directory.
    /// </summary>
    [Fact]
    public void An_empty_clips_directory_yields_exactly_two_findings_for_the_two_shipped_reactions()
    {
        string emptyDirectory = Path.Combine(Path.GetTempPath(), "sse-story-check-empty-" + Guid.NewGuid());
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            List<StoryFinding> findings = FindingsAcrossShippedScenarios(emptyDirectory);

            findings.Count.ShouldBe(2);
            findings.ShouldAllBe(f => f.Trigger == "inference/ObjectDetected");
        }
        finally
        {
            Directory.Delete(emptyDirectory, recursive: true);
        }
    }

    private static List<StoryFinding> FindingsAcrossShippedScenarios(string clipsDirectory)
    {
        ScenarioOptions options = LoadShippedScenarios();

        List<StoryFinding> findings = [];
        foreach ((string key, ScenarioDefinition scenario) in options.Scenarios)
        {
            Dictionary<string, ClipManifestLoadResult> manifests = scenario.Assets
                .Select(asset => asset.Camera.Clip)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(clip => clip, clip => ClipManifestLoader.Load(clipsDirectory, clip), StringComparer.Ordinal);

            findings.AddRange(ScenarioStoryCheck.Find(key, scenario.Assets, manifests));
        }

        return findings;
    }

    /// <summary>
    /// Loads the real <c>Scenarios/*.json</c> the same way
    /// <c>ScenarioFileTests.Load</c> and <c>LegacyHighlightRuleBodyTests.LoadShippedScenarios</c>
    /// do (duplicated here rather than exposed from either, which are
    /// otherwise untouched by this PR).
    /// </summary>
    private static ScenarioOptions LoadShippedScenarios()
    {
        string scenarios = Path.Combine(AppContext.BaseDirectory, "Scenarios");

        Microsoft.Extensions.Configuration.IConfigurationBuilder builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);

        foreach (string file in Directory.EnumerateFiles(scenarios, "*.json"))
        {
            builder.AddJsonFile(file, optional: false);
        }

        ScenarioOptions options = new();
        builder.Build().GetSection(ScenarioOptions.SectionName).Bind(options);
        return options;
    }

    private static AssetDefinition Asset(string key, string clip) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = clip },
    };

    private static ReactionDefinition Reaction(string name, string source, string kind) => new()
    {
        Name = name,
        When = new ReactionTrigger { Source = source, Kind = kind, Predicate = "$.payload.class == 'x'" },
        Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
    };

    private static SensorDefinition Sensor(string kind, string source) => new()
    {
        Kind = kind,
        Unit = "unit",
        Behaviour = "steady",
        Source = source,
        Mean = 1,
    };

    private static HighlightDefinition Highlight(string triggerKind) => new()
    {
        TriggerKind = triggerKind,
        Comparison = "gte",
        Threshold = 1,
        DurationMs = 1_000,
    };

    private static CueDefinition Cue(string source, string kind) => new(AtMs: 0, Class: "x", Confidence: 0.9, Kind: kind, Source: source);

    private static Dictionary<string, ClipManifestLoadResult> ManifestFor(string clip, CueDefinition cue) =>
        new(StringComparer.Ordinal)
        {
            [clip] = new ClipManifestLoadResult(Option<ClipManifest>.Some(new ClipManifest(clip, 20_000, [cue])), []),
        };

    /// <summary>No sidecar for this asset's clip — the normal, unannotated case.</summary>
    private static Dictionary<string, ClipManifestLoadResult> NoManifests(AssetDefinition asset) =>
        new(StringComparer.Ordinal)
        {
            [asset.Camera.Clip] = new ClipManifestLoadResult(Option<ClipManifest>.None, []),
        };
}
