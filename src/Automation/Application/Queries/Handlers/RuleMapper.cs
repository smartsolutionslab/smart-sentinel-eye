using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

/// <summary>
/// Domain → wire projection for rules, shared by the get and list handlers so
/// the two cannot drift apart.
/// </summary>
internal static class RuleMapper
{
    public static RuleDto Map(Rule rule) =>
        new(
            RuleIdentifier: rule.Id.Value,
            Version: rule.Version,
            Name: rule.Name.Value,
            TriggerSource: rule.TriggerSource,
            TriggerKind: rule.TriggerKind,
            Predicate: rule.Predicate.Value,
            Action: MapAction(rule.Action),
            State: rule.State.Value,
            CreatedAt: rule.CreatedAt,
            CreatedBy: rule.CreatedBy.Value,
            PublishedAt: rule.PublishedAt,
            ArchivedAt: rule.ArchivedAt);

    public static RuleActionDto MapAction(RuleAction action) =>
        action switch
        {
            RuleAction.SetVariableValue setValue =>
                RuleActionDto.ForSetVariableValue(setValue.VariableName, setValue.ValueExpression),
            RuleAction.HighlightOverlay highlight =>
                RuleActionDto.ForHighlightOverlay(highlight.Overlay, highlight.DurationMs),
            // A new variant must be given a wire shape deliberately rather than
            // silently serialising as an empty object.
            _ => throw new NotSupportedException(
                $"No wire shape defined for rule action '{action.GetType().Name}'."),
        };
}
