namespace SmartSentinelEye.ScenarioSimulator.Scenario;

/// <summary>
/// A declared reaction on an asset (spec 289 plan.md §3.3): fires
/// <see cref="Then"/> when <see cref="When"/> matches an event on this
/// asset's device. Bound the same mutable-POCO way as
/// <see cref="ScenarioOptions"/>'s other types, because <c>IOptions</c>
/// binding needs it.
/// </summary>
public sealed class ReactionDefinition
{
    public string Name { get; set; } = string.Empty;

    public ReactionTrigger When { get; set; } = new();

    public ReactionAction Then { get; set; } = new();
}

/// <summary>What must be true of an event for <see cref="ReactionDefinition.Then"/> to fire.</summary>
public sealed class ReactionTrigger
{
    /// <summary>Event source (<c>plc</c> | <c>inference</c>), taken verbatim — not derived from a sensor.</summary>
    public string Source { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    /// <summary>AEL predicate fragment, wrapped in the asset's own device guard by <c>ReactionRuleSeed</c>.</summary>
    public string Predicate { get; set; } = string.Empty;
}

/// <summary>
/// What a reaction does. Only <c>Type == "HighlightOverlay"</c> is
/// recognised by <c>ReactionRuleSeed</c> today; <c>SetVariableValue</c>'s
/// fields are declared here now so this shared, non-contention shape does
/// not need touching again when its seeding support lands.
/// </summary>
public sealed class ReactionAction
{
    public string Type { get; set; } = string.Empty;

    /// <summary><c>HighlightOverlay</c>: how long the overlay flashes.</summary>
    public int? DurationMs { get; set; }

    /// <summary><c>SetVariableValue</c>: the variable this reaction writes.</summary>
    public string? Variable { get; set; }

    /// <summary><c>SetVariableValue</c>: the AEL field-select expression to write.</summary>
    public string? Value { get; set; }
}
