namespace SmartSentinelEye.ScenarioSimulator.Scenario;

/// <summary>
/// A system variable declared on an asset (spec 289 plan.md §3.3). Seeded via
/// <c>SystemVariablesClient.EnsureVariableAsync</c> before the asset's overlay
/// and rules, so a <c>SetVariableValue</c> reaction always has somewhere to
/// write. Bound the same mutable-POCO way as <see cref="ReactionDefinition"/>,
/// because <c>IOptions</c> binding needs it.
/// </summary>
public sealed class VariableDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Wire type: <c>String</c> | <c>Number</c> | <c>Boolean</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Required iff <see cref="Type"/> is <c>Boolean</c>.</summary>
    public string? TruthyLabel { get; set; }

    /// <summary>Required iff <see cref="Type"/> is <c>Boolean</c>.</summary>
    public string? FalsyLabel { get; set; }
}
