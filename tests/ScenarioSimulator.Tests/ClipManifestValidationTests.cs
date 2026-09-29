using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B02 (ADR-0144 red). <c>ClipManifestValidation</c> (pure,
/// plan.md §5.3) and <c>ClipManifestLoader</c> (I/O) do not exist yet; this
/// class is the target shape (plan §4, T-B10).
///
/// <para>
/// <c>ClipManifestValidation.Validate(manifest, clip)</c> returns every
/// violation found — it does not stop at the first — each naming the offending
/// cue's index where the violation is cue-scoped (<c>CueViolation.CueIndex</c>),
/// or <c>null</c> for a manifest-level violation (a bad <c>Clip</c> name or
/// <c>DurationMs</c>). FR-003: any violation refuses the <b>whole</b> sidecar,
/// so the caller (<c>ClipManifestLoader</c>) never returns a manifest with the
/// violating cues silently dropped.
/// </para>
///
/// <para>
/// <c>ClipManifestLoader.Load(clipsDirectory, clip)</c> reads
/// <c>{clipsDirectory}/{Path.GetFileNameWithoutExtension(clip)}.cues.json</c>.
/// A missing file is normal (<c>Manifest = Option.None</c>, no violations). An
/// existing-but-unparseable file is one violation. An existing, parseable but
/// invalid file surfaces <c>Validate</c>'s violations. Only a parseable and
/// valid file returns <c>Manifest = Option.Some(...)</c>.
/// </para>
/// </summary>
public sealed class ClipManifestValidationTests
{
    [Fact]
    public void A_manifest_with_every_field_in_range_yields_no_violations()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(AtMs: 4_000, Class: "person", Confidence: 0.55, Zone: "walkway"),
            new CueDefinition(
                AtMs: 12_000, Class: "person", Confidence: 0.92, Zone: "exclusion",
                Label: "PERSON IN EXCLUSION ZONE",
                Box: new CueBox(X: 0.61, Y: 0.40, Width: 0.08, Height: 0.31)),
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_clip_name_that_does_not_match_the_sidecars_own_basename_is_a_manifest_level_violation()
    {
        ClipManifest manifest = new("some-other-clip.mp4", 20_000, [new CueDefinition(0, "person", 0.9)]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        CueViolation violation = violations.ShouldHaveSingleItem();
        violation.CueIndex.ShouldBeNull();
    }

    [Fact]
    public void A_non_positive_duration_is_a_manifest_level_violation()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 0, [new CueDefinition(0, "person", 0.9)]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == null);
    }

    [Fact]
    public void A_cue_at_or_past_the_clips_duration_is_refused_and_names_its_index()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(4_000, "person", 0.9),
            new CueDefinition(25_000, "person", 0.9), // >= DurationMs
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 1);
    }

    [Fact]
    public void Cues_out_of_ascending_order_are_refused_and_name_the_offending_index()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(12_000, "person", 0.9),
            new CueDefinition(4_000, "person", 0.9), // out of order
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 1);
    }

    [Fact]
    public void A_blank_class_is_refused_and_names_its_index()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000, [new CueDefinition(0, "  ", 0.9)]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void A_confidence_outside_0_to_1_is_refused_and_names_its_index(double confidence)
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000, [new CueDefinition(0, "person", confidence)]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    [Fact]
    public void A_box_whose_extent_exceeds_the_normalised_frame_is_refused()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(0, "person", 0.9, Box: new CueBox(X: 0.9, Y: 0.1, Width: 0.5, Height: 0.1)), // X+Width > 1
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    [Fact]
    public void A_source_other_than_plc_or_inference_is_refused()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(0, "person", 0.9, Source: "camera"),
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    [Fact]
    public void A_kind_longer_than_128_characters_is_refused()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000,
        [
            new CueDefinition(0, "person", 0.9, Kind: new string('K', 129)),
        ]);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    [Fact]
    public void A_manifest_with_more_than_10_cues_per_second_of_clip_is_refused()
    {
        // 1_000ms clip -> density limit is ceil(1000/1000) * 10 = 10 cues.
        CueDefinition[] cues = [.. Enumerable.Range(0, 11).Select(i => new CueDefinition(i * 50, "person", 0.9))];
        ClipManifest manifest = new("mill-roughing.mp4", 1_000, cues);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Backend-reviewer finding B2 (blocker), probe 1: a sidecar with no
    /// <c>Cues</c> property at all deserializes to a <c>ClipManifest</c> whose
    /// <c>Cues</c> is <c>null</c> (System.Text.Json leaves a missing
    /// reference-typed record parameter at its default). <c>Validate</c> must
    /// refuse this with a violation, not throw <see cref="NullReferenceException"/>
    /// from <c>manifest.Cues.Count</c> — a throw here happens inside
    /// <c>ClipManifestLoader.Load</c>, called unguarded from
    /// <c>ClipCueHostedService.ResolveCuedPaths</c> (no per-asset try around
    /// it), so it takes the whole <c>BackgroundService</c> down —
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c> stops the seeder,
    /// the billet timeline and the Wolverine consumer along with it.
    /// </summary>
    [Fact]
    public void A_manifest_whose_Cues_list_is_null_is_refused_rather_than_throwing()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000, Cues: null!);

        IReadOnlyList<CueViolation> violations = Should.NotThrow(
            () => ClipManifestValidation.Validate(manifest, "mill-roughing.mp4"));

        violations.ShouldNotBeEmpty();
    }

    /// <summary>
    /// B2, probe 2: <c>"Cues":[null]</c> is valid JSON — a null array element —
    /// and deserializes to a list containing a null <c>CueDefinition</c>.
    /// <c>ValidateOneCue</c> must refuse that entry, not throw
    /// <see cref="NullReferenceException"/> from <c>cue.AtMs</c>.
    /// </summary>
    [Fact]
    public void A_null_entry_within_Cues_is_refused_rather_than_throwing()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000, [null!]);

        IReadOnlyList<CueViolation> violations = Should.NotThrow(
            () => ClipManifestValidation.Validate(manifest, "mill-roughing.mp4"));

        violations.ShouldContain(v => v.CueIndex == 0);
    }

    /// <summary>
    /// B2, probe 3: an empty <c>"Cues": []</c> currently passes <c>Validate</c>
    /// (the density check and the per-cue loop both no-op on zero cues), and
    /// the manifest is then handed to <c>CueSchedule.Next</c> as
    /// <c>Option.Some</c>. Every tick then indexes <c>manifest.Cues[0]</c> on a
    /// zero-length list and throws — forever, once per second, on that path.
    /// <b>Judgment call:</b> refusing outright (rather than silently treating
    /// it as "no manifest") is chosen because a sidecar that declares zero
    /// cues is very likely an authoring mistake, and a warning naming the clip
    /// is more useful than the sidecar quietly having no effect.
    /// </summary>
    [Fact]
    public void A_manifest_declaring_no_cues_at_all_is_refused()
    {
        ClipManifest manifest = new("mill-roughing.mp4", 20_000, []);

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, "mill-roughing.mp4");

        violations.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_sidecar_gives_no_manifest_and_no_violations()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            ClipManifestLoadResult result = ClipManifestLoader.Load(clipsDirectory, "no-such-clip.mp4");

            result.Manifest.HasValue.ShouldBeFalse();
            result.Violations.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Unparseable_JSON_is_one_violation_and_yields_no_manifest()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(clipsDirectory, "broken.cues.json"), "{ not valid json ");

            ClipManifestLoadResult result = ClipManifestLoader.Load(clipsDirectory, "broken.mp4");

            result.Manifest.HasValue.ShouldBeFalse();
            result.Violations.ShouldHaveSingleItem();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_valid_sidecar_loads_to_Some_with_no_violations()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "mill-roughing.cues.json"),
                """
                {
                  "Clip": "mill-roughing.mp4",
                  "DurationMs": 20000,
                  "Cues": [
                    { "AtMs": 12000, "Class": "person", "Confidence": 0.92 }
                  ]
                }
                """);

            ClipManifestLoadResult result = ClipManifestLoader.Load(clipsDirectory, "mill-roughing.mp4");

            result.Violations.ShouldBeEmpty();
            result.Manifest.HasValue.ShouldBeTrue();
            result.Manifest.Value.Cues.ShouldHaveSingleItem();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task An_invalid_but_parseable_sidecar_surfaces_Validates_violations_and_yields_no_manifest()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "mill-roughing.cues.json"),
                """
                {
                  "Clip": "mill-roughing.mp4",
                  "DurationMs": 20000,
                  "Cues": [
                    { "AtMs": 25000, "Class": "person", "Confidence": 0.92 }
                  ]
                }
                """);

            ClipManifestLoadResult result = ClipManifestLoader.Load(clipsDirectory, "mill-roughing.mp4");

            result.Manifest.HasValue.ShouldBeFalse();
            result.Violations.ShouldContain(v => v.CueIndex == 0);
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    /// <summary>
    /// B2, probe 1, through the real pipeline: the exact JSON body the
    /// reviewer probed with, read off disk via <c>ClipManifestLoader.Load</c>
    /// rather than a hand-built <c>ClipManifest</c>, confirming System.Text.Json
    /// really does leave <c>Cues</c> null for a missing property (not an empty
    /// list) and that the loader surfaces a violation rather than propagating
    /// the throw.
    /// </summary>
    [Fact]
    public async Task A_sidecar_with_no_Cues_property_at_all_is_refused_rather_than_throwing()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "mill-roughing.cues.json"),
                """{ "Clip": "mill-roughing.mp4", "DurationMs": 1000 }""");

            ClipManifestLoadResult result = Should.NotThrow(
                () => ClipManifestLoader.Load(clipsDirectory, "mill-roughing.mp4"));

            result.Manifest.HasValue.ShouldBeFalse();
            result.Violations.ShouldNotBeEmpty();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    /// <summary>
    /// B2, probe 3 (loader): plan.md §5.3 says "an unreadable or unparseable
    /// JSON file is one violation" — but <c>Load</c>'s try/catch names only
    /// <see cref="System.Text.Json.JsonException"/>. A sidecar that exists but
    /// cannot be read (locked by another process, here — the same shape as a
    /// permissions failure) throws <see cref="IOException"/> out of
    /// <c>File.ReadAllText</c>, uncaught, with the same host-crashing
    /// consequence as B2's other probes.
    /// </summary>
    [Fact]
    public void An_unreadable_sidecar_is_one_violation_rather_than_a_thrown_exception()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        string sidecar = Path.Combine(clipsDirectory, "mill-roughing.cues.json");
        File.WriteAllText(sidecar, """{ "Clip": "mill-roughing.mp4", "DurationMs": 1000, "Cues": [] }""");

        try
        {
            using FileStream exclusiveLock = new(sidecar, FileMode.Open, FileAccess.Read, FileShare.None);

            ClipManifestLoadResult result = Should.NotThrow(
                () => ClipManifestLoader.Load(clipsDirectory, "mill-roughing.mp4"));

            result.Manifest.HasValue.ShouldBeFalse();
            result.Violations.ShouldHaveSingleItem();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    private static string CreateTempClipsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sse-cues-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        return directory;
    }
}
