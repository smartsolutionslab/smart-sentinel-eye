using System.Text.Json;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>
/// The result of <see cref="ClipManifestLoader.Load"/>: either a valid
/// manifest with no violations, or no manifest and the violations that
/// refused it (empty when there simply was no sidecar to read).
/// </summary>
public sealed record ClipManifestLoadResult(Option<ClipManifest> Manifest, IReadOnlyList<CueViolation> Violations);

/// <summary>
/// Reads and validates a clip's <c>&lt;clip&gt;.cues.json</c> sidecar (spec
/// 289 FR-001, plan.md §5.3). A missing sidecar is normal — most clips have
/// none annotated yet — and yields no manifest and no violations. An existing
/// but unparseable file is one violation. An existing, parseable but invalid
/// file surfaces <see cref="ClipManifestValidation"/>'s violations. Only a
/// parseable and valid file yields a manifest.
/// </summary>
public static class ClipManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ClipManifestLoadResult Load(string clipsDirectory, string clip)
    {
        Ensure.That(clip).IsNotNull();

        if (string.IsNullOrWhiteSpace(clipsDirectory))
        {
            return new ClipManifestLoadResult(Option<ClipManifest>.None, []);
        }

        string sidecar = Path.Combine(clipsDirectory, Path.GetFileNameWithoutExtension(clip) + ".cues.json");
        if (!File.Exists(sidecar))
        {
            return new ClipManifestLoadResult(Option<ClipManifest>.None, []);
        }

        ClipManifest? manifest;
        try
        {
            string json = File.ReadAllText(sidecar);
            manifest = JsonSerializer.Deserialize<ClipManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return new ClipManifestLoadResult(
                Option<ClipManifest>.None, [new CueViolation(null, $"'{sidecar}' is not valid JSON: {exception.Message}")]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ClipManifestLoadResult(
                Option<ClipManifest>.None, [new CueViolation(null, $"'{sidecar}' could not be read: {exception.Message}")]);
        }

        if (manifest is null)
        {
            return new ClipManifestLoadResult(
                Option<ClipManifest>.None, [new CueViolation(null, $"'{sidecar}' did not deserialize to a manifest.")]);
        }

        IReadOnlyList<CueViolation> violations = ClipManifestValidation.Validate(manifest, clip);
        return violations.Count > 0
            ? new ClipManifestLoadResult(Option<ClipManifest>.None, violations)
            : new ClipManifestLoadResult(Option<ClipManifest>.Some(manifest), []);
    }

    /// <summary>Every distinct clip's manifest, loaded once and keyed by clip file name.</summary>
    public static Dictionary<string, ClipManifestLoadResult> LoadManifestsByClip(IEnumerable<string> clips, string clipsDirectory)
    {
        Ensure.That(clips).IsNotNull();
        Ensure.That(clipsDirectory).IsNotNull();

        return clips.Distinct(StringComparer.Ordinal).ToDictionary(clip => clip, clip => Load(clipsDirectory, clip), StringComparer.Ordinal);
    }
}
