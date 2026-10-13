using System.Reflection;
using SmartSentinelEye.Automation.Api;
using SmartSentinelEye.Automation.Api.Requests;
using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>RulesEndpoints.BuildAction</c> builds and refuses (spec 341, #2896), so that
/// splitting its switch into one method per arm is a change of shape only.
///
/// <para>
/// <b>Reflection, deliberately.</b> <c>BuildAction</c> is private, and the alternative is a
/// full-stack test through <c>POST /rules</c> that needs the Aspire stack. The method keeps its
/// name and signature through the refactor, so the binding survives it. The messages matter
/// because they reach callers as the <c>400 RULE_INVALID_INPUT</c> <c>detail</c>.
/// </para>
/// </summary>
public class RuleActionBuildCharacterisationTests
{
    private static readonly Guid OverlayGuid = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static readonly Guid WallGuid = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static readonly Guid LayoutGuid = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static readonly MethodInfo BuildActionMethod =
        typeof(RulesEndpoints).GetMethod("BuildAction", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RulesEndpoints.BuildAction was not found.");

    [Fact]
    public void A_SetVariableValue_request_builds_a_SetVariableValue_action()
    {
        var action = Build(Request("SetVariableValue", variableName: "line.state", valueExpression: "event.payload.state"));

        var built = action.ShouldBeOfType<RuleAction.SetVariableValue>();
        built.VariableName.ShouldBe("line.state");
        built.ValueExpression.ShouldBe("event.payload.state");
    }

    [Fact]
    public void A_HighlightOverlay_request_builds_a_HighlightOverlay_action()
    {
        var action = Build(Request("HighlightOverlay", overlayIdentifier: OverlayGuid, durationMs: 1500));

        var built = action.ShouldBeOfType<RuleAction.HighlightOverlay>();
        built.Overlay.Value.ShouldBe(OverlayGuid);
        built.Duration.Value.ShouldBe(1500);
    }

    [Fact]
    public void A_SwitchWallScene_request_with_a_Next_target_builds_a_Next_scene()
    {
        var action = Build(Request("SwitchWallScene", wallIdentifier: WallGuid, sceneTarget: "Next"));

        var built = action.ShouldBeOfType<RuleAction.SwitchWallScene>();
        built.Wall.Value.ShouldBe(WallGuid);
        built.Target.ShouldBeOfType<SceneTarget.Next>();
    }

    [Fact]
    public void A_SwitchWallScene_request_with_a_Layout_target_builds_a_Layout_scene()
    {
        var action = Build(
            Request("SwitchWallScene", wallIdentifier: WallGuid, sceneTarget: "Layout", targetLayoutIdentifier: LayoutGuid));

        var built = action.ShouldBeOfType<RuleAction.SwitchWallScene>();
        built.Wall.Value.ShouldBe(WallGuid);
        built.Target.ShouldBeOfType<SceneTarget.Layout>().Value.Value.ShouldBe(LayoutGuid);
    }

    [Fact]
    public void A_SetVariableValue_request_without_a_variable_name_is_refused_by_name()
    {
        Refusal(Request("SetVariableValue", valueExpression: "1"))
            .ShouldBe("VariableName is required for SetVariableValue actions.");
    }

    [Fact]
    public void A_SetVariableValue_request_without_a_value_expression_is_refused_by_name()
    {
        Refusal(Request("SetVariableValue", variableName: "line.state"))
            .ShouldBe("ValueExpression is required for SetVariableValue actions.");
    }

    [Fact]
    public void A_HighlightOverlay_request_without_an_overlay_is_refused_by_name()
    {
        Refusal(Request("HighlightOverlay", durationMs: 1500))
            .ShouldBe("OverlayIdentifier is required for HighlightOverlay actions.");
    }

    [Fact]
    public void A_HighlightOverlay_request_without_a_duration_is_refused_by_name()
    {
        Refusal(Request("HighlightOverlay", overlayIdentifier: OverlayGuid))
            .ShouldBe("DurationMs is required for HighlightOverlay actions.");
    }

    [Fact]
    public void A_SwitchWallScene_request_without_a_wall_is_refused_by_name()
    {
        Refusal(Request("SwitchWallScene", sceneTarget: "Next"))
            .ShouldBe("WallIdentifier is required for SwitchWallScene actions.");
    }

    [Fact]
    public void A_SwitchWallScene_request_without_a_scene_target_is_refused_by_name()
    {
        Refusal(Request("SwitchWallScene", wallIdentifier: WallGuid))
            .ShouldBe("SceneTarget is required for SwitchWallScene actions.");
    }

    [Fact]
    public void An_unknown_action_type_is_refused_naming_the_type_and_the_valid_ones()
    {
        Refusal(Request("Explode"))
            .ShouldBe("Unknown ActionType 'Explode'. Expected: SetVariableValue | HighlightOverlay | SwitchWallScene.");
    }

    [Fact]
    public void A_SetVariableValue_request_missing_both_fields_is_refused_on_the_variable_name_first()
    {
        Refusal(Request("SetVariableValue"))
            .ShouldBe("VariableName is required for SetVariableValue actions.");
    }

    [Fact]
    public void A_HighlightOverlay_request_missing_both_fields_is_refused_on_the_overlay_first()
    {
        Refusal(Request("HighlightOverlay"))
            .ShouldBe("OverlayIdentifier is required for HighlightOverlay actions.");
    }

    [Fact]
    public void A_SwitchWallScene_request_missing_both_fields_is_refused_on_the_wall_first()
    {
        Refusal(Request("SwitchWallScene"))
            .ShouldBe("WallIdentifier is required for SwitchWallScene actions.");
    }

    [Fact]
    public void A_SwitchWallScene_request_with_a_Next_target_tolerates_an_absent_layout()
    {
        var action = Build(Request("SwitchWallScene", wallIdentifier: WallGuid, sceneTarget: "Next"));

        action.ShouldBeOfType<RuleAction.SwitchWallScene>();
    }

    private static RuleAction Build(CreateRuleRequest body) =>
        (RuleAction)BuildActionMethod.Invoke(null, [body])!;

    private static string Refusal(CreateRuleRequest body)
    {
        var thrown = Should.Throw<TargetInvocationException>(() => BuildActionMethod.Invoke(null, [body]));

        return thrown.InnerException.ShouldBeOfType<ArgumentException>().Message;
    }

    private static CreateRuleRequest Request(
        string actionType,
        string? variableName = null,
        string? valueExpression = null,
        Guid? overlayIdentifier = null,
        int? durationMs = null,
        Guid? wallIdentifier = null,
        string? sceneTarget = null,
        Guid? targetLayoutIdentifier = null) =>
        new(
            "rule",
            "source",
            "kind",
            "true",
            actionType,
            variableName,
            valueExpression,
            overlayIdentifier,
            durationMs,
            wallIdentifier,
            sceneTarget,
            targetLayoutIdentifier);
}
