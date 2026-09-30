using System.Globalization;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Builds the <see cref="RuleSeed"/> for the legacy <c>Highlight</c> field
/// (ADR-0111 M2, spec 289 FR-009), or refuses it — it never throws. Mirrors
/// <see cref="ReactionRuleSeed"/>'s <c>Valid</c>/<c>Refused</c> shape (issue
/// #2698): an unmapped <see cref="HighlightDefinition.Comparison"/> used to
/// fall back silently to <c>&gt;=</c>; it is refused instead, naming the bad
/// comparison text.
/// </summary>
internal static class HighlightRuleSeed
{
    public static HighlightSeedResult From(string scenario, AssetDefinition asset, Guid overlay)
    {
        HighlightDefinition highlight = asset.Highlight!;
        Ensure.That(highlight).IsNotNull();

        string assetKey = asset.Camera.Path;

        string triggerSource = asset.Sensors
                .FirstOrDefault(sensor => string.Equals(sensor.Kind, highlight.TriggerKind, StringComparison.Ordinal))?.Source
            ?? "plc";

        string? comparisonOperator = Operator(highlight.Comparison);
        if (comparisonOperator is null)
        {
            return new HighlightSeedResult.Refused(
                $"comparison '{highlight.Comparison}' is not a recognised operator.");
        }

        string predicate =
            $"$.device == '{assetKey}' && $.payload.value {comparisonOperator} {highlight.Threshold.ToString(CultureInfo.InvariantCulture)}";

        RuleSeed seed = new(
            $"{scenario}-{asset.Key}-highlight",
            triggerSource,
            highlight.TriggerKind,
            predicate,
            new RuleSeedAction.HighlightOverlay(overlay, highlight.DurationMs));
        return new HighlightSeedResult.Valid(seed);
    }

    private static string? Operator(string comparison) => (comparison ?? string.Empty).ToLowerInvariant() switch
    {
        "gte" => ">=",
        "lte" => "<=",
        "gt" => ">",
        "lt" => "<",
        "eq" => "==",
        "ne" => "!=",
        _ => null,
    };
}

/// <summary>Whether the legacy <c>Highlight</c> field produced a seedable <see cref="RuleSeed"/>.</summary>
internal abstract record HighlightSeedResult
{
    internal sealed record Valid(RuleSeed Seed) : HighlightSeedResult;

    internal sealed record Refused(string Reason) : HighlightSeedResult;
}
