using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Api;

/// <summary>Rule action builders for <see cref="RulesEndpoints"/>.</summary>
public static partial class RulesEndpoints
{
    private static RuleAction BuildAction(CreateRuleRequest body)
    {
        return body.ActionType switch
        {
            SetVariableValue => BuildSetVariableValue(body),
            HighlightOverlay => BuildHighlightOverlay(body),
            SwitchWallScene => BuildSwitchWallScene(body),
            _ => throw new ArgumentException($"Unknown ActionType '{body.ActionType}'. Expected: SetVariableValue | HighlightOverlay | SwitchWallScene."),
        };
    }

    private static RuleAction.SetVariableValue BuildSetVariableValue(CreateRuleRequest body)
    {
        return RuleAction.SetVariableValue.From(
            body.VariableName ?? throw new ArgumentException("VariableName is required for SetVariableValue actions."),
            body.ValueExpression ?? throw new ArgumentException("ValueExpression is required for SetVariableValue actions."));
    }

    private static RuleAction.HighlightOverlay BuildHighlightOverlay(CreateRuleRequest body)
    {
        return RuleAction.HighlightOverlay.From(
            body.OverlayIdentifier ?? throw new ArgumentException("OverlayIdentifier is required for HighlightOverlay actions."),
            body.DurationMs ?? throw new ArgumentException("DurationMs is required for HighlightOverlay actions."));
    }

    private static RuleAction.SwitchWallScene BuildSwitchWallScene(CreateRuleRequest body)
    {
        return RuleAction.SwitchWallScene.From(
            body.WallIdentifier ?? throw new ArgumentException("WallIdentifier is required for SwitchWallScene actions."),
            body.SceneTarget ?? throw new ArgumentException("SceneTarget is required for SwitchWallScene actions."),
            body.TargetLayoutIdentifier);
    }
}
