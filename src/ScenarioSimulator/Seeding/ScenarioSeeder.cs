using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Background service that, once on startup, reads the active scenario and seeds
/// it (ADR-0111). For each asset it seeds its declared variables first, then the
/// per-station overlay (Phase A, capturing its id + tile for the wall join) + the
/// highlight rule (Phase B), then registers the camera (Phase C) — which publishes
/// <c>CameraRegisteredV1</c>, consumed by <c>CameraRegisteredSimHandler</c> to
/// provision the loop path and, once all four stations are complete, create the
/// single 2×2 wall (Phase D). Idempotent throughout (stable names; 409/existing →
/// reuse), so a restart re-syncs without duplicating.
/// </summary>
public sealed class ScenarioSeeder(
    CameraCatalogClient catalog,
    OverlayDesignerClient overlays,
    AutomationRulesClient rules,
    SystemVariablesClient variables,
    AssetCorrelationTable correlation,
    IOptions<ScenarioOptions> scenarioOptions,
    WallSeeder wall,
    ILogger<ScenarioSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ScenarioOptions scenarios = scenarioOptions.Value;
        int failed = 0;

        // Every active scenario gets its cameras, overlays and rules. One missing
        // key skips only itself, so a typo in the list costs one plant and not
        // the run.
        foreach (string key in scenarios.Active)
        {
            if (!scenarios.Scenarios.TryGetValue(key, out ScenarioDefinition? scenario))
            {
                logger.ScenarioNotFound(key);
                continue;
            }

            logger.SeedingScenario(scenario.Name, scenario.Assets.Count);

            // Loaded once per scenario and shared with every asset below:
            // the highlight's triggerSource derivation and the reaction
            // manifest pre-filter would otherwise each read the same
            // sidecar again. Scoped like the per-asset loop below, for the
            // same reason (StopHost) — an I/O or JSON fault here must cost
            // this scenario, not the whole simulator.
            Dictionary<string, ClipManifestLoadResult> manifestsByClip;
            try
            {
                manifestsByClip = ClipManifestLoader.LoadManifestsByClip(
                    scenario.Assets.Select(asset => asset.Camera.Clip), scenarios.ClipsDirectory);
                ReportUnreachableTriggers(key, scenario.Assets, manifestsByClip);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.ScenarioManifestLoadFailed(key, ex.Message);
                continue;
            }

            foreach (AssetDefinition asset in scenario.Assets)
            {
                // Scoped, not swallowed. This is a `BackgroundService`, so an
                // exception escaping here trips
                // `BackgroundServiceExceptionBehavior.StopHost` and kills the whole
                // simulator — the billet timeline, the CameraRegisteredV1 consumer
                // and the rest of the seeding pass — while every other service
                // stays healthy and the stack looks fine. That happened on a
                // transient 30 s HTTP timeout, and the timeouts recur on every
                // boot; only whether the retries fit the budget varies.
                //
                // So one asset's failure costs that asset. It is logged as a
                // warning naming the scenario, the asset and the cause, and the
                // run is reported as incomplete at the end — nothing here is
                // silent, which is the distinction the constitution draws.
                // `CameraSimReconciler` already treats a camera-sim outage this
                // way, and the guards inside `CameraCatalogClient` and
                // `OverlayDesignerClient` exist for exactly this reason, one call
                // at a time; this is the same protection at the loop.
                try
                {
                    await SeedOverlayAndRuleAsync(key, asset, manifestsByClip, stoppingToken);

                    // Record the id whether the camera was created or already
                    // existed: it is what correlates the camera to its wall tile,
                    // and CameraRegisteredV1 only supplies it for a genuinely new
                    // one. Null means the read-back could not determine it (logged
                    // there); the wall simply stays incomplete rather than the seed
                    // failing.
                    Guid? camera = await catalog.RegisterCameraAsync(asset.Name, asset.Camera.Path, stoppingToken);
                    if (camera.HasValue)
                    {
                        correlation.RecordCamera(asset.Camera.Path, camera.Value);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    logger.AssetSeedFailed(key, asset.Key, ex.Message);
                }
            }

            logger.ScenarioSeeded(scenario.Name);
        }

        // The event handler covers live registrations; this covers the restart
        // where every camera already exists, so no event fires and the walls
        // would otherwise never be rebuilt. Both are idempotent. Once, after all
        // scenarios: it tries every wall and skips the incomplete ones.
        try
        {
            await wall.TryCreateAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed++;
            logger.WallSeedFailed(ex.Message);
        }

        // Said once, at the end, so a partial seed is a statement rather than
        // something a reader has to reconstruct from scattered warnings. A stack
        // whose simulator seeded eight of twelve assets looks identical from the
        // outside to one that seeded all twelve.
        if (failed > 0)
        {
            logger.SeedingIncomplete(failed);
        }
    }

    /// <summary>
    /// Seeds one asset's declared variables, overlay and highlight rule. Every
    /// name it creates is prefixed with <paramref name="scenario"/> — the
    /// literal used to be "rolling-mill" because there was only ever one
    /// plant, and three plants sharing an overlay name would each overwrite
    /// the last.
    /// </summary>
    private async Task SeedOverlayAndRuleAsync(
        string scenario,
        AssetDefinition asset,
        IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip,
        CancellationToken cancellationToken)
    {
        // Variables first (plan.md §5.4): a SetVariableValue reaction needs
        // to know, before any rule is seeded, which of this asset's declared
        // variables actually exist server-side.
        HashSet<string> seededVariables = await SeedVariablesAsync(asset, cancellationToken);

        if (asset.Overlay is null || asset.Tile is null)
        {
            // No overlay to key a HighlightOverlay rule to, but a
            // SetVariableValue reaction needs none — ReactionRuleSeed.From
            // already refuses HighlightOverlay on a null overlay and nothing
            // else, so only that subset is lost here (S2).
            await SeedReactionsAsync(scenario, asset, overlay: null, seededVariables, manifestsByClip, cancellationToken);
            return;
        }

        string assetKey = asset.Camera.Path;
        OverlayLabel label = new(
            asset.Overlay.Label,
            (decimal)asset.Overlay.X,
            (decimal)asset.Overlay.Y,
            (decimal)asset.Overlay.Width,
            (decimal)asset.Overlay.Height,
            (int)asset.Overlay.FontSize);

        Guid overlay = await overlays.EnsureOverlayAsync($"{scenario}-{asset.Key}", [label], cancellationToken);
        correlation.RecordOverlay(scenario, assetKey, overlay, asset.Tile.Row, asset.Tile.Col);

        if (asset.Highlight is null)
        {
            logger.AssetMissingField(asset.Key, "highlight");
        }
        else
        {
            IReadOnlyList<CueDefinition> cues = ScenarioStoryCheck.ValidCuesFor(asset, manifestsByClip);
            switch (HighlightRuleSeed.From(scenario, asset, overlay, cues))
            {
                case HighlightSeedResult.Refused refused:
                    logger.HighlightRuleRefused(asset.Key, refused.Reason);
                    break;
                case HighlightSeedResult.Valid valid:
                    RuleSeedResult highlightResult = await rules.EnsureRuleAsync(valid.Seed, cancellationToken);
                    if (highlightResult is RuleSeedResult.Refused highlightRefused)
                    {
                        logger.HighlightRuleRefused(asset.Key, highlightRefused.Reason);
                    }

                    break;
            }
        }

        await SeedReactionsAsync(scenario, asset, overlay, seededVariables, manifestsByClip, cancellationToken);
    }

    /// <summary>
    /// Seeds <see cref="AssetDefinition.Variables"/> (plan.md §3.3, §5.4) and
    /// returns the names that ended up existing server-side — either created
    /// or reused via a 409 read-back. A name whose seed was refused (a type
    /// mismatch against an already-existing variable) is absent from the
    /// result, which is exactly what <see cref="SeedReactionsAsync"/> needs to
    /// decide whether a <c>SetVariableValue</c> reaction has somewhere to write.
    /// <para>
    /// S1 (backend-reviewer, spec 289). Caught per variable, not left to
    /// the per-asset backstop: variables now seed first, so an I/O failure that
    /// propagated from here would cost the overlay, the legacy highlight, every
    /// reaction and the camera registration too, not just this one variable. A
    /// failed variable is treated as not-seeded — <see cref="SeedReactionsAsync"/>
    /// already skips exactly its dependent reactions via <paramref name="asset"/>-scoped
    /// <c>seededVariables</c>, so nothing else on the asset is lost. This is a
    /// deliberate departure from plan.md §5.4 as originally written, which
    /// allowed the broader blast radius.
    /// </para>
    /// </summary>
    private async Task<HashSet<string>> SeedVariablesAsync(AssetDefinition asset, CancellationToken cancellationToken)
    {
        HashSet<string> seeded = new(StringComparer.Ordinal);

        foreach (VariableDefinition variable in asset.Variables)
        {
            VariableSeedResult result;
            try
            {
                result = await variables.EnsureVariableAsync(variable, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.VariableSeedFailed(variable.Name, ex.Message);
                continue;
            }

            if (result is VariableSeedResult.Seeded)
            {
                seeded.Add(variable.Name);
            }
        }

        return seeded;
    }

    /// <summary>
    /// Seeds <see cref="AssetDefinition.Reactions"/> in file order (plan.md
    /// §5.4), after the legacy <see cref="HighlightDefinition"/>. A refused
    /// reaction is logged by name and skipped; the others still seed. A
    /// reaction whose trigger only a cue could satisfy is also skipped,
    /// without ever reaching <see cref="ReactionRuleSeed"/>, when this
    /// asset's clip's sidecar was itself refused (spec 289 US1, "malformed
    /// manifest") — nothing on the asset will ever emit that trigger, so
    /// seeding it would be a rule that can never fire.
    /// <para>
    /// S3 (backend-reviewer, spec 289). <see cref="ReactionRuleSeed.From"/>
    /// runs first, so its own refusal reasons (blank/undeclared variable) win
    /// over the generic "was not seeded" message; only a <em>valid</em>
    /// <c>SetVariableValue</c> seed whose variable <paramref name="seededVariables"/>
    /// does not record is skipped here — it, and only it, not every reaction on
    /// the asset (mirroring the manifest-refusal pre-filter above). A refused
    /// <see cref="RuleSeedResult"/> from <c>AutomationRulesClient</c> (issue B1,
    /// e.g. a predicate that fails to parse) is logged and skipped the same way.
    /// </para>
    /// </summary>
    private async Task SeedReactionsAsync(
        string scenario,
        AssetDefinition asset,
        Guid? overlay,
        HashSet<string> seededVariables,
        IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip,
        CancellationToken cancellationToken)
    {
        if (asset.Reactions.Count == 0)
        {
            return;
        }

        // Loaded once per scenario by the caller, not reloaded here.
        ClipManifestLoadResult clip = manifestsByClip[asset.Camera.Clip];
        bool clipRefused = clip.Violations.Count > 0;

        foreach (ReactionDefinition reaction in asset.Reactions)
        {
            (string source, string kind) = (reaction.When.Source, reaction.When.Kind);

            bool sensorSatisfiesTrigger = asset.Sensors.Any(sensor =>
                string.Equals(sensor.Source, source, StringComparison.Ordinal)
                && string.Equals(sensor.Kind, kind, StringComparison.Ordinal));
            bool cueSatisfiesTrigger = clip.Manifest.HasValue && clip.Manifest.Value.Cues.Any(cue =>
                string.Equals(cue.Source, source, StringComparison.Ordinal)
                && string.Equals(cue.Kind, kind, StringComparison.Ordinal));

            if (!sensorSatisfiesTrigger && !cueSatisfiesTrigger && clipRefused)
            {
                logger.ReactionSkippedRefusedManifest(asset.Key, reaction.Name, source, kind);
                continue;
            }

            ReactionSeedResult result = ReactionRuleSeed.From(scenario, asset, reaction, overlay);
            switch (result)
            {
                case ReactionSeedResult.Valid { Seed.Action: RuleSeedAction.SetVariableValue setVariable }
                    when !seededVariables.Contains(setVariable.VariableName):
                    logger.ReactionSkippedVariableRefused(asset.Key, reaction.Name, setVariable.VariableName);
                    break;
                case ReactionSeedResult.Valid valid:
                    RuleSeedResult ruleResult = await rules.EnsureRuleAsync(valid.Seed, cancellationToken);
                    if (ruleResult is RuleSeedResult.Refused ruleRefused)
                    {
                        logger.ReactionRefused(asset.Key, reaction.Name, ruleRefused.Reason);
                    }

                    break;
                case ReactionSeedResult.Refused refused:
                    logger.ReactionRefused(asset.Key, reaction.Name, refused.Reason);
                    break;
            }
        }
    }

    /// <summary>Logs one <see cref="Log.ScenarioReactionUnreachable"/> warning per finding. Still seeds.</summary>
    private void ReportUnreachableTriggers(
        string scenario, List<AssetDefinition> assets, IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip)
    {
        foreach (StoryFinding finding in ScenarioStoryCheck.Find(scenario, assets, manifestsByClip))
        {
            logger.ScenarioReactionUnreachable(finding.Scenario, finding.Asset, finding.Reaction, finding.Trigger);
        }
    }
}
