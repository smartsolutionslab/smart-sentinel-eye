using SmartSentinelEye.ScenarioSimulator.Cues;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Characterisation of <c>ClipManifestValidation.Validate</c> (spec 342, behaviour-preserving):
/// every assertion is on the whole violation list, so message text, interpolation and the order of
/// checks are all pinned. Confidence values are integer-valued so the expected text does not depend
/// on the current culture.
/// </summary>
public sealed class ClipManifestValidationOrderingTests
{
    private const string Clip = "mill-roughing.mp4";
    private const int Duration = 20_000;

    private static CueDefinition ValidCue(int atMs) => new(atMs, "person", 1);

    private static ClipManifest ManifestWith(params CueDefinition?[] cues) =>
        new(Clip, Duration, cues!);

    private static List<CueViolation> Validate(ClipManifest manifest) =>
        [.. ClipManifestValidation.Validate(manifest, Clip)];

    [Fact]
    public void Manifest_level_violations_are_reported_clip_then_duration_then_null_cues()
    {
        ClipManifest manifest = new("other.mp4", 0, null!);

        var violations = Validate(manifest);

        violations.ShouldBe(
        [
            new CueViolation(null, "Clip 'other.mp4' does not match the sidecar's own clip 'mill-roughing.mp4'."),
            new CueViolation(null, "DurationMs 0 must be positive."),
            new CueViolation(null, "Cues must not be null."),
        ]);
    }

    [Fact]
    public void A_clip_mismatch_with_no_cues_reports_the_mismatch_then_the_empty_cue_list()
    {
        ClipManifest manifest = new("other.mp4", Duration, []);

        var violations = Validate(manifest);

        violations.ShouldBe(
        [
            new CueViolation(null, "Clip 'other.mp4' does not match the sidecar's own clip 'mill-roughing.mp4'."),
            new CueViolation(null, "Cues must declare at least one cue."),
        ]);
    }

    [Fact]
    public void More_cues_than_the_density_limit_report_one_density_violation()
    {
        var cues = Enumerable.Range(0, 11).Select(index => ValidCue(index * 50)).ToArray();
        ClipManifest manifest = new(Clip, 1000, cues);

        var violations = Validate(manifest);

        violations.ShouldBe(
        [
            new CueViolation(null, "11 cue(s) exceed the density limit of 10 for a 1000ms clip."),
        ]);
    }

    [Fact]
    public void A_cue_breaking_every_rule_reports_range_ascending_class_confidence_box_source_kind_in_that_order()
    {
        var offender = new CueDefinition(
            AtMs: -5,
            Class: " ",
            Confidence: 2,
            Kind: "",
            Source: "camera",
            Box: new CueBox(0.9, 0.1, 0.5, 0.1));
        var manifest = ManifestWith(ValidCue(0), offender);

        var violations = Validate(manifest);

        violations.ShouldBe(
        [
            new CueViolation(1, "AtMs -5 is out of range for a 20000ms clip."),
            new CueViolation(1, "AtMs -5 is not strictly ascending after 0."),
            new CueViolation(1, "Class must not be blank."),
            new CueViolation(1, "Confidence 2 must be between 0 and 1."),
            new CueViolation(1, "Box extends beyond the normalised frame."),
            new CueViolation(1, "Source 'camera' must be 'plc' or 'inference'."),
            new CueViolation(1, "Kind must be non-blank and at most 128 characters."),
        ]);
    }

    [Fact]
    public void A_null_cue_is_reported_and_the_next_cue_is_ordered_against_the_last_non_null_one()
    {
        var manifest = ManifestWith(ValidCue(5000), null, ValidCue(1000));

        var violations = Validate(manifest);

        violations.ShouldBe(
        [
            new CueViolation(1, "Cue must not be null."),
            new CueViolation(2, "AtMs 1000 is not strictly ascending after 5000."),
        ]);
    }

    [Fact]
    public void A_cue_at_the_clip_duration_is_out_of_range()
    {
        var manifest = ManifestWith(ValidCue(Duration));

        Validate(manifest).ShouldBe([new CueViolation(0, "AtMs 20000 is out of range for a 20000ms clip.")]);
    }

    [Fact]
    public void Cues_at_zero_and_one_millisecond_before_the_end_are_accepted()
    {
        var manifest = ManifestWith(ValidCue(0), ValidCue(Duration - 1));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void Two_cues_at_the_same_offset_are_not_strictly_ascending()
    {
        var manifest = ManifestWith(ValidCue(100), ValidCue(100));

        Validate(manifest).ShouldBe([new CueViolation(1, "AtMs 100 is not strictly ascending after 100.")]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Confidence_at_either_end_of_the_unit_interval_is_accepted(int confidence)
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", confidence));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void A_box_touching_the_frame_edges_is_accepted()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Box: new CueBox(0.5, 0.5, 0.5, 0.5)));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void A_cue_without_a_box_is_accepted()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Box: null));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void A_box_with_a_negative_height_is_refused()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Box: new CueBox(0.1, 0.1, 0.1, -0.1)));

        Validate(manifest).ShouldBe([new CueViolation(0, "Box extends beyond the normalised frame.")]);
    }

    [Fact]
    public void The_plc_source_is_accepted()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Source: "plc"));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void A_null_source_is_refused_with_an_empty_quoted_value()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Source: null!));

        Validate(manifest).ShouldBe([new CueViolation(0, "Source '' must be 'plc' or 'inference'.")]);
    }

    [Fact]
    public void A_kind_of_exactly_128_characters_is_accepted()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Kind: new string('k', 128)));

        Validate(manifest).ShouldBeEmpty();
    }

    [Fact]
    public void A_kind_of_129_characters_is_refused()
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Kind: new string('k', 129)));

        Validate(manifest).ShouldBe(
            [new CueViolation(0, "Kind must be non-blank and at most 128 characters.")]);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_whitespace_or_null_kind_is_refused_without_throwing(string? kind)
    {
        var manifest = ManifestWith(new CueDefinition(0, "person", 1, Kind: kind!));

        Validate(manifest).ShouldBe(
            [new CueViolation(0, "Kind must be non-blank and at most 128 characters.")]);
    }

    [Fact]
    public void A_non_positive_duration_skips_the_range_and_density_checks()
    {
        ClipManifest manifest = new(Clip, 0, [ValidCue(50_000)]);

        Validate(manifest).ShouldBe([new CueViolation(null, "DurationMs 0 must be positive.")]);
    }
}
