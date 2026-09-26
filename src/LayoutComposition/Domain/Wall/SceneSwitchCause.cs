using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Who or what moved <c>Showing</c> (spec 258 FR-006, ADR-0157
/// §Consequences). Carried on <see cref="Events.WallSceneSwitchedDomainEvent"/>
/// and mapped onto <c>WallSceneChangedV1</c>'s flat
/// <c>Cause</c>/<c>Rule</c>/<c>CausingEventIdentifier</c> shape by the
/// Application-layer domain-event handler — never serialised directly.
/// </summary>
public abstract record SceneSwitchCause
{
    private SceneSwitchCause() { }

    /// <summary>A manual switch through management-web (PD-4).</summary>
    public sealed record Operator(OperatorIdentifier By) : SceneSwitchCause;

    /// <summary>
    /// An Automation rule fired (US2, PD-1). Carries both the rule and the
    /// plant-floor event that triggered it, per ADR-0157 §Consequences.
    /// </summary>
    public sealed record Rule(RuleIdentifier RuleIdentifier, CausingEventIdentifier CausingEventIdentifier) : SceneSwitchCause;

    /// <summary>
    /// Editing the scene set dropped the scene that was showing, so the
    /// pointer moved to the new first scene (US1-15). Not an operator's
    /// deliberate switch, but still attributable to whoever made the edit.
    /// </summary>
    public sealed record Reconfigured(OperatorIdentifier By) : SceneSwitchCause;
}
