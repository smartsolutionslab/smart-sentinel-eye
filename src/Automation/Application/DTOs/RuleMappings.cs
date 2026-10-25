using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.DTOs;

internal static class RuleMappings
{
    public static RuleDto ToDto(this Rule rule) =>
        new(
            RuleIdentifier: rule.Id.Value,
            Version: rule.Version,
            Fab: rule.Fab.Value,
            Name: rule.Name.Value,
            TriggerSource: rule.TriggerSource.Value,
            TriggerKind: rule.TriggerKind.Value,
            Predicate: rule.Predicate.Value,
            Action: rule.Action.ToDto(),
            State: rule.State.Value,
            CreatedAt: rule.Creation.At,
            CreatedBy: rule.Creation.By.Value,
            PublishedAt: rule.PublishedAt?.Value,
            ArchivedAt: rule.ArchivedAt?.Value);

    public static RuleActionDto ToDto(this RuleAction action) =>
        action switch
        {
            RuleAction.SetVariableValue setValue =>
                RuleActionDto.ForSetVariableValue(setValue.VariableName, setValue.ValueExpression),
            RuleAction.HighlightOverlay highlight =>
                RuleActionDto.ForHighlightOverlay(highlight.Overlay.Value, highlight.Duration.Value),
            RuleAction.SwitchWallScene switchWallScene =>
                RuleActionDto.ForSwitchWallScene(
                    switchWallScene.Wall.Value,
                    SceneTargetWireMapping.Literal(switchWallScene.Target),
                    SceneTargetWireMapping.TargetLayout(switchWallScene.Target)),
            // A new variant must be given a wire shape deliberately rather than
            // silently serialising as an empty object.
            _ => throw new NotSupportedException(
                $"No wire shape defined for rule action '{action.GetType().Name}'."),
        };
}
