using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Discriminated VO of action shapes (spec 007 FR-009). Two
/// variants in v1: <see cref="SetVariableValue"/> and
/// <see cref="HighlightOverlay"/>.
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
public abstract record RuleAction
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
}
