using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Discriminated VO of action shapes (spec 007 FR-009). Two
/// variants in v1: <see cref="SetVariableValue"/> and
/// <see cref="HighlightOverlay"/>.
///
/// <para>
/// The action carries primitive types only — Automation never
/// references SystemVariables.Domain or OverlayDesigner.Domain. The
/// referenced names + identifiers are validated at the API edge
/// (cross-context boundary) and passed through.
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
            string n = Ensure.That(variableName, nameof(variableName))
                .IsNotNullOrWhiteSpace()
                .HasMaxLength(VariableNameMaximumLength)
                .AndReturn();
            string e = Ensure.That(valueExpression, nameof(valueExpression))
                .IsNotNullOrWhiteSpace()
                .HasMaxLength(ValueExpressionMaximumLength)
                .AndReturn();
            return new SetVariableValue(n, e);
        }
    }

    /// <summary>
    /// Asks LayoutComposition to push an
    /// <c>OverlayHighlightChanged</c> SignalR frame to every kiosk
    /// rendering the affected overlay. The kiosk applies the
    /// <c>ssE-overlay-highlight</c> CSS class for
    /// <see cref="DurationMs"/> milliseconds.
    /// </summary>
    public sealed record HighlightOverlay(Guid Overlay, int DurationMs) : RuleAction
    {
        public const int MinimumDurationMs = 500;
        public const int MaximumDurationMs = 60_000;

        public static HighlightOverlay From(Guid overlay, int durationMs)
        {
            if (overlay == Guid.Empty)
            {
                throw new ArgumentException("Overlay identifier cannot be empty.", nameof(overlay));
            }
            if (durationMs < MinimumDurationMs || durationMs > MaximumDurationMs)
            {
                throw new ArgumentException($"durationMs must be between {MinimumDurationMs} and {MaximumDurationMs}; got {durationMs}.", nameof(durationMs));
            }
            return new HighlightOverlay(overlay, durationMs);
        }
    }
}
