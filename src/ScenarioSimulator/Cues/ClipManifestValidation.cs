using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>
/// One violation found in a <see cref="ClipManifest"/>. <see cref="CueIndex"/>
/// names the offending cue, or is <c>null</c> for a manifest-level violation
/// (a bad <c>Clip</c> name, a non-positive <c>DurationMs</c>, or too many
/// cues for the clip's length).
/// </summary>
public sealed record CueViolation(int? CueIndex, string Message);

/// <summary>
/// Pure validation of a <see cref="ClipManifest"/> against spec 289's rules
/// (plan.md §5.3). Collects every violation — it does not stop at the first
/// — because FR-003 refuses the whole sidecar on any violation, and a caller
/// reporting them wants the full list, not just the first offender.
/// </summary>
public static class ClipManifestValidation
{
    /// <summary>Density limit (FR-003): at most this many cues per second of clip.</summary>
    private const int MaxCuesPerSecond = 10;

    private const int MaxKindLength = 128;

    public static IReadOnlyList<CueViolation> Validate(ClipManifest manifest, string clip)
    {
        Ensure.That(manifest).IsNotNull();
        Ensure.That(clip).IsNotNull();

        List<CueViolation> violations = [];

        if (!string.Equals(manifest.Clip, clip, StringComparison.Ordinal))
        {
            violations.Add(new CueViolation(
                null, $"Clip '{manifest.Clip}' does not match the sidecar's own clip '{clip}'."));
        }

        if (manifest.DurationMs <= 0)
        {
            violations.Add(new CueViolation(null, $"DurationMs {manifest.DurationMs} must be positive."));
        }

        // A manifest whose Cues property is absent from the JSON deserializes
        // with Cues left at its default (null) — the record's declared
        // non-nullable type does not stop that at runtime. Nothing past this
        // point can be checked meaningfully, so this refuses on its own.
        if (manifest.Cues is null)
        {
            violations.Add(new CueViolation(null, "Cues must not be null."));
            return violations;
        }

        if (manifest.Cues.Count == 0)
        {
            // A sidecar declaring zero cues is very likely an authoring
            // mistake (a copy-pasted skeleton, a forgotten annotation pass):
            // refusing it with a named warning is more useful than letting
            // it silently have no effect.
            violations.Add(new CueViolation(null, "Cues must declare at least one cue."));
        }
        else if (manifest.DurationMs > 0)
        {
            var densityLimit = (int)Math.Ceiling(manifest.DurationMs / 1000.0) * MaxCuesPerSecond;
            if (manifest.Cues.Count > densityLimit)
            {
                violations.Add(new CueViolation(
                    null,
                    $"{manifest.Cues.Count} cue(s) exceed the density limit of {densityLimit} "
                    + $"for a {manifest.DurationMs}ms clip."));
            }
        }

        ValidateCues(manifest, violations);

        return violations;
    }

    private static void ValidateCues(ClipManifest manifest, List<CueViolation> violations)
    {
        int? previousAtMs = null;

        for (var index = 0; index < manifest.Cues.Count; index++)
        {
            var cue = manifest.Cues[index];
            if (cue is null)
            {
                violations.Add(new CueViolation(index, "Cue must not be null."));
                continue;
            }

            AddRangeViolation(cue, index, manifest.DurationMs, violations);
            AddOrderViolation(cue, index, previousAtMs, violations);
            ValidateCueContent(cue, index, violations);
            previousAtMs = cue.AtMs;
        }
    }

    private static void AddRangeViolation(
        CueDefinition cue,
        int index,
        int durationMs,
        List<CueViolation> violations)
    {
        if (cue.AtMs < 0 || (durationMs > 0 && cue.AtMs >= durationMs))
        {
            violations.Add(new CueViolation(index, $"AtMs {cue.AtMs} is out of range for a {durationMs}ms clip."));
        }
    }

    private static void AddOrderViolation(
        CueDefinition cue,
        int index,
        int? previousAtMs,
        List<CueViolation> violations)
    {
        if (previousAtMs is not null && cue.AtMs <= previousAtMs.Value)
        {
            violations.Add(new CueViolation(
                index, $"AtMs {cue.AtMs} is not strictly ascending after {previousAtMs.Value}."));
        }
    }

    private static void ValidateCueContent(CueDefinition cue, int index, List<CueViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(cue.Class))
        {
            violations.Add(new CueViolation(index, "Class must not be blank."));
        }

        if (cue.Confidence is < 0 or > 1)
        {
            violations.Add(new CueViolation(index, $"Confidence {cue.Confidence} must be between 0 and 1."));
        }

        if (cue.Box is { } box && ExtendsBeyondFrame(box))
        {
            violations.Add(new CueViolation(index, "Box extends beyond the normalised frame."));
        }

        if (cue.Source is not ("plc" or "inference"))
        {
            violations.Add(new CueViolation(index, $"Source '{cue.Source}' must be 'plc' or 'inference'."));
        }

        if (IsInvalidKind(cue.Kind))
        {
            violations.Add(new CueViolation(index, $"Kind must be non-blank and at most {MaxKindLength} characters."));
        }
    }

    private static bool ExtendsBeyondFrame(CueBox box) =>
        box.X < 0 || box.Y < 0 || box.Width < 0 || box.Height < 0
        || box.X + box.Width > 1 || box.Y + box.Height > 1;

    private static bool IsInvalidKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind) || kind.Length > MaxKindLength;
}
