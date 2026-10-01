using SmartSentinelEye.Shared.Kernel.Primitives;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Discriminated VO of action shapes (spec 007 FR-009). Three
/// variants: <see cref="SetVariableValue"/>, <see cref="HighlightOverlay"/>
/// and <see cref="SwitchWallScene"/> (spec 296, ADR-0157 §2).
///
/// <para>
/// Automation never references SystemVariables.Domain or
/// OverlayDesigner.Domain, and each <c>From</c> takes the primitive it was
/// handed at the API edge. That is a rule about **project references**, not
/// about the types an action stores: an overlay reference is a context-local
/// <see cref="OverlayIdentifier"/> and a highlight window a
/// <see cref="HighlightDuration"/>, both declared here. The variable name and
/// AEL expression stay strings — SystemVariables validates the first when it
/// consumes the effect, and the second is source text this context compiles.
/// </para>
/// </summary>
public abstract record RuleAction : IValueObject
{
    /// <summary>
    /// Sets a system variable's value to the result of evaluating
    /// <see cref="ValueExpression"/> (AEL) against the triggering
    /// event's envelope + payload. The downstream
    /// SystemVariables consumer coerces the result to the
    /// variable's declared type.
    /// </summary>
    public sealed record SetVariableValue(string VariableName, string ValueExpression) : RuleAction
    {
        public const int VariableNameMaximumLength = 64;
        public const int ValueExpressionMaximumLength = 4096;

        public static SetVariableValue From(string variableName, string valueExpression)
        {
            Ensure.That(variableName, nameof(variableName))
                .IsNotNullOrWhiteSpace()
                .HasMaxLength(VariableNameMaximumLength);
            Ensure.That(valueExpression, nameof(valueExpression))
                .IsNotNullOrWhiteSpace()
                .HasMaxLength(ValueExpressionMaximumLength);
            return new SetVariableValue(variableName, valueExpression);
        }
    }

    /// <summary>
    /// Asks LayoutComposition to push an
    /// <c>OverlayHighlightChanged</c> SignalR frame to every kiosk
    /// rendering the affected overlay. The kiosk applies the
    /// <c>ssE-overlay-highlight</c> CSS class for
    /// <see cref="DurationMs"/> milliseconds.
    /// </summary>
    public sealed record HighlightOverlay(OverlayIdentifier Overlay, HighlightDuration Duration) : RuleAction
    {
        public static HighlightOverlay From(Guid overlay, int durationMs) =>
            new(OverlayIdentifier.From(overlay), HighlightDuration.From(durationMs));
    }

    /// <summary>
    /// Asks LayoutComposition to switch <see cref="Wall"/> to
    /// <see cref="Target"/> when the rule fires (spec 296 US1, ADR-0157 §2).
    /// </summary>
    public sealed record SwitchWallScene(WallIdentifier Wall, SceneTarget Target) : RuleAction
    {
        /// <summary>
        /// Parses the API edge's raw <c>wallIdentifier</c> /
        /// <c>sceneTarget</c> / <c>targetLayoutIdentifier</c> triple
        /// (US2-7). Throws <see cref="ArgumentException"/> for every bad
        /// shape — a missing wall, a target other than exactly
        /// <see cref="SceneTarget.NextLiteral"/> or
        /// <see cref="SceneTarget.LayoutLiteral"/>, a <c>Layout</c> target
        /// with no layout, a <c>Next</c> target carrying one, or an empty
        /// layout guid — which <c>RulesEndpoints</c> already maps to
        /// <c>400 RULE_INVALID_INPUT</c>.
        /// </summary>
        public static SwitchWallScene From(Guid wall, string target, Guid? layout)
        {
            WallIdentifier wallIdentifier = WallIdentifier.From(wall);

            return target switch
            {
                SceneTarget.NextLiteral when layout is not null =>
                    throw new ArgumentException(
                        "A Next target must not carry a targetLayoutIdentifier.", nameof(layout)),
                SceneTarget.NextLiteral =>
                    new SwitchWallScene(wallIdentifier, new SceneTarget.Next()),
                SceneTarget.LayoutLiteral when layout is null =>
                    throw new ArgumentException(
                        "A Layout target requires a targetLayoutIdentifier.", nameof(layout)),
                SceneTarget.LayoutLiteral =>
                    new SwitchWallScene(wallIdentifier, new SceneTarget.Layout(LayoutIdentifier.From(layout.Value))),
                _ => throw new ArgumentException(
                    $"Unknown sceneTarget '{target}'. Expected: {SceneTarget.NextLiteral} | {SceneTarget.LayoutLiteral}.",
                    nameof(target)),
            };
        }
    }
}
