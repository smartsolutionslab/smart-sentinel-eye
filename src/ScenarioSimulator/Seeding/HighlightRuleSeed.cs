using System.Globalization;
using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Builds the <see cref="RuleSeed"/> for the legacy <c>Highlight</c> field
/// (ADR-0111 M2, spec 289 FR-009). Reproduces, byte-for-byte, what
/// <c>ScenarioSeeder.SeedOverlayAndRuleAsync</c> built inline before PR-A —
/// including the <see cref="Operator"/> switch's silent <c>&gt;=</c> default
/// for an unmapped comparison, which is a known defect kept out of this
/// characterisation refactor on purpose (plan.md §3.4, follow-up F-2).
/// </summary>
internal static class HighlightRuleSeed
{
    public static RuleSeed From(string scenario, AssetDefinition asset, Guid overlay)
    {
        string assetKey = asset.Camera.Path;

        string triggerSource = asset.Sensors
                .FirstOrDefault(sensor => string.Equals(sensor.Kind, asset.Highlight!.TriggerKind, StringComparison.Ordinal))?.Source
            ?? "plc";

        string predicate =
            $"$.device == '{assetKey}' && $.payload.value {Operator(asset.Highlight!.Comparison)} {asset.Highlight.Threshold.ToString(CultureInfo.InvariantCulture)}";

        return new RuleSeed(
            $"{scenario}-{asset.Key}-highlight",
            triggerSource,
            asset.Highlight.TriggerKind,
            predicate,
            new RuleSeedAction.HighlightOverlay(overlay, asset.Highlight.DurationMs));
    }

    private static string Operator(string comparison) => (comparison ?? string.Empty).ToLowerInvariant() switch
    {
        "gte" => ">=",
        "lte" => "<=",
        "gt" => ">",
        "lt" => "<",
        "eq" => "==",
        "ne" => "!=",
        _ => ">=",
    };
}
