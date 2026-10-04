namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// The first set-level violation found by <see cref="Overlay.ValidateLabels"/>
/// (spec 150, ADR-0164). Modelled on <c>GridViolation</c>
/// (<c>LayoutComposition.Domain.Layout</c>): command handlers map each case to
/// its <c>OVERLAY_LABELS_*</c> <c>400</c> error — an operator input error is a
/// <see cref="Shared.Kernel.Result{TValue,TError}"/> failure, not a thrown
/// exception (ADR-0047).
///
/// <para>
/// That is the operator-facing tier. Underneath it, <see cref="Overlay"/>
/// enforces the same check on itself via the private backstop guard called
/// from <see cref="Overlay.CreateDraft"/> and <see cref="Overlay.EditDraft"/>,
/// thrown as <see cref="System.InvalidOperationException"/> — reached only
/// when a caller skips the handler's validation, so it is a programmer-error
/// backstop, not a second operator-facing path.
/// </para>
///
/// <para>
/// Deliberately no <c>Overlap</c>/<c>DuplicateGeometry</c> case: two labels
/// sharing a position or text is allowed (FR-004) and is #2348's territory,
/// not a defect this spec prevents.
/// </para>
/// </summary>
public enum LabelSetViolation
{
    /// <summary>A revision must carry at least one label.</summary>
    Empty,

    /// <summary>The label count exceeds <see cref="Label.MaxLabels"/>.</summary>
    TooMany,
}
