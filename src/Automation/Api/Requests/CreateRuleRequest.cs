namespace SmartSentinelEye.Automation.Api.Requests;

/// <summary>
/// HTTP body shape for <c>POST /rules</c> (spec 007 US1 /
/// FR-023). <see cref="ActionType"/> selects which optional fields
/// are required: <c>SetVariableValue</c> needs
/// <see cref="VariableName"/> + <see cref="ValueExpression"/>;
/// <c>HighlightOverlay</c> needs <see cref="OverlayIdentifier"/>
/// + <see cref="DurationMs"/>; <c>SwitchWallScene</c> (spec 296) needs
/// <see cref="WallIdentifier"/> + <see cref="SceneTarget"/>
/// (<c>"Next"</c> | <c>"Layout"</c>), plus
/// <see cref="TargetLayoutIdentifier"/> exactly when
/// <see cref="SceneTarget"/> is <c>"Layout"</c>.
/// </summary>
public sealed record CreateRuleRequest(
    string Name,
    string TriggerSource,
    string TriggerKind,
    string Predicate,
    string ActionType,
    string? VariableName,
    string? ValueExpression,
    Guid? OverlayIdentifier,
    int? DurationMs,
    Guid? WallIdentifier,
    string? SceneTarget,
    Guid? TargetLayoutIdentifier);
