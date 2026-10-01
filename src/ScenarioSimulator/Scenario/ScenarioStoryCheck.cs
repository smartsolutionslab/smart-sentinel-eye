using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Scenario;

/// <summary>
/// One trigger that nothing on its asset will ever emit (plan.md §5.5): a
/// declared <see cref="ReactionDefinition"/>, named by its own
/// <see cref="ReactionDefinition.Name"/>, or the legacy
/// <see cref="HighlightDefinition"/>, named <c>"highlight"</c> (it declares
/// no name of its own). <see cref="Trigger"/> is <c>"{Source}/{Kind}"</c> for
/// a reaction and bare <c>Kind</c> for the highlight, matching how each is
/// matched in <see cref="ScenarioStoryCheck.Find"/>.
/// </summary>
public sealed record StoryFinding(string Scenario, string Asset, string Reaction, string Trigger);

/// <summary>
/// Pure check (plan.md §5.5): for each asset, the set of triggers something
/// will actually emit is <c>{(sensor.Source, sensor.Kind)}</c> union
/// <c>{(cue.Source, cue.Kind)}</c> for its clip's <em>valid</em> manifest —
/// a refused or missing sidecar contributes nothing, indistinguishable from
/// each other today. A declared <see cref="ReactionDefinition"/> is matched
/// by <c>(Source, Kind)</c>; the legacy <see cref="HighlightDefinition"/>
/// declares no <c>Source</c> of its own, so it is matched by <c>Kind</c>
/// alone. Neither match stops the reaction or the highlight from being
/// seeded — this only reports.
/// <para>
/// Takes each clip's already-loaded <see cref="ClipManifestLoadResult"/>
/// rather than touching disk itself, keyed by <see cref="CameraDefinition.Clip"/>,
/// so a caller seeding the scenario loads each manifest once and shares it
/// with this check.
/// </para>
/// </summary>
public static class ScenarioStoryCheck
{
    private const string HighlightReactionName = "highlight";

    public static IReadOnlyList<StoryFinding> Find(
        string scenario,
        IReadOnlyList<AssetDefinition> assets,
        IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip)
    {
        Ensure.That(scenario).IsNotNull();
        Ensure.That(assets).IsNotNull();
        Ensure.That(manifestsByClip).IsNotNull();

        List<StoryFinding> findings = [];

        foreach (AssetDefinition asset in assets)
        {
            IReadOnlyList<CueDefinition> cues = ValidCuesFor(asset, manifestsByClip);

            findings.AddRange(asset.Reactions
                .Where(reaction => !EmitsSourceAndKind(asset, cues, reaction.When.Source, reaction.When.Kind))
                .Select(reaction => new StoryFinding(
                    scenario, asset.Key, reaction.Name, $"{reaction.When.Source}/{reaction.When.Kind}")));

            if (asset.Highlight is not null
                && MatchedSource(asset, cues, asset.Highlight.TriggerKind) is null)
            {
                findings.Add(new StoryFinding(scenario, asset.Key, HighlightReactionName, asset.Highlight.TriggerKind));
            }
        }

        return findings;
    }

    /// <summary>
    /// The legacy highlight's <c>triggerSource</c> (plan.md §5.5): a sensor
    /// of the matching <paramref name="kind"/> if one exists, else a valid
    /// cue's own <see cref="CueDefinition.Source"/>, else <c>"plc"</c> — the
    /// value the silent fallback used to produce, now reported by
    /// <see cref="Find"/> rather than hidden when nothing actually matches.
    /// </summary>
    public static string DeriveHighlightTriggerSource(AssetDefinition asset, string kind, IReadOnlyList<CueDefinition> cues)
    {
        Ensure.That(asset).IsNotNull();
        Ensure.That(cues).IsNotNull();

        return MatchedSource(asset, cues, kind) ?? "plc";
    }

    /// <summary>
    /// A clip's valid-manifest cues, or none when its sidecar was missing or
    /// refused. Shared by <see cref="Find"/> and by callers deriving a
    /// highlight's <c>triggerSource</c>, so "valid manifest" means one thing
    /// everywhere.
    /// </summary>
    public static IReadOnlyList<CueDefinition> ValidCuesFor(
        AssetDefinition asset, IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip)
    {
        Ensure.That(asset).IsNotNull();
        Ensure.That(manifestsByClip).IsNotNull();

        return manifestsByClip.TryGetValue(asset.Camera.Clip, out ClipManifestLoadResult? result) && result.Manifest.HasValue
            ? result.Manifest.Value.Cues
            : [];
    }

    private static bool EmitsSourceAndKind(AssetDefinition asset, IReadOnlyList<CueDefinition> cues, string source, string kind) =>
        asset.Sensors.Any(sensor =>
            string.Equals(sensor.Source, source, StringComparison.Ordinal) && string.Equals(sensor.Kind, kind, StringComparison.Ordinal))
        || cues.Any(cue =>
            string.Equals(cue.Source, source, StringComparison.Ordinal) && string.Equals(cue.Kind, kind, StringComparison.Ordinal));

    /// <summary>
    /// A sensor's or a valid cue's own <c>Source</c> for <paramref name="kind"/>,
    /// matched by kind alone — the legacy highlight declares no source of its
    /// own (plan.md §5.5), unlike a reaction's exact <c>(Source, Kind)</c>
    /// match in <see cref="EmitsSourceAndKind"/>.
    /// </summary>
    private static string? MatchedSource(AssetDefinition asset, IReadOnlyList<CueDefinition> cues, string kind) =>
        asset.Sensors.FirstOrDefault(sensor => string.Equals(sensor.Kind, kind, StringComparison.Ordinal))?.Source
        ?? cues.FirstOrDefault(cue => string.Equals(cue.Kind, kind, StringComparison.Ordinal))?.Source;
}
