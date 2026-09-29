using System.Globalization;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Builds the <see cref="RuleSeed"/> for the legacy <c>Highlight</c> field
/// (ADR-0111 M2, spec 289 FR-009). Reproduces, byte-for-byte, what
/// <c>ScenarioSeeder.SeedOverlayAndRuleAsync</c> built inline before this
/// refactor — including the <see cref="Operator"/> switch's silent
/// <c>&gt;=</c> default for an unmapped comparison, which is a known defect
/// deliberately kept out of this characterisation refactor.
/// </summary>
internal static class HighlightRuleSeed
{
    public static RuleSeed From(string scenario, AssetDefinition asset, Guid overlay)
    {
        HighlightDefinition highlight = asset.Highlight!;
        Ensure.That(highlight).IsNotNull();

        string assetKey = asset.Camera.Path;

        string triggerSource = asset.Sensors
                .FirstOrDefault(sensor => string.Equals(sensor.Kind, highlight.TriggerKind, StringComparison.Ordinal))?.Source
            ?? "plc";

        string predicate =
            $"$.device == '{assetKey}' && $.payload.value {Operator(highlight.Comparison)} {highlight.Threshold.ToString(CultureInfo.InvariantCulture)}";

        return new RuleSeed(
            $"{scenario}-{asset.Key}-highlight",
            triggerSource,
            highlight.TriggerKind,
            predicate,
            new RuleSeedAction.HighlightOverlay(overlay, highlight.DurationMs));
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
