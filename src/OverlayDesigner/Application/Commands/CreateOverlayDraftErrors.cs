using System.Net;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="CreateOverlayDraftCommand"/> (ADR-0047 + ADR-0089).
/// </summary>
public abstract record CreateOverlayDraftError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNameTaken(string Name)
        : CreateOverlayDraftError(
            "OVERLAY_NAME_TAKEN",
            $"A non-archived overlay with the name '{Name}' already exists.",
            HttpStatusCode.Conflict);

    /// <summary>Spec 150 FR-003 — a revision must carry at least one label.</summary>
    public sealed record EmptyLabelSet()
        : CreateOverlayDraftError(
            "OVERLAY_LABELS_EMPTY",
            "A revision must carry at least one label.",
            HttpStatusCode.BadRequest);

    /// <summary>Spec 150 FR-002, ADR-0164 — the label set exceeds the ceiling.</summary>
    public sealed record TooManyLabels(int Count)
        : CreateOverlayDraftError(
            "OVERLAY_LABELS_TOO_MANY",
            $"A revision may carry at most {Label.MaxLabels} labels; {Count} were submitted.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="CreateOverlayDraftError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class CreateOverlayDraftFailures
{
    public static CreateOverlayDraftError OverlayNameTaken(string name) =>
        new CreateOverlayDraftError.OverlayNameTaken(name);

    public static CreateOverlayDraftError EmptyLabelSet() =>
        new CreateOverlayDraftError.EmptyLabelSet();

    public static CreateOverlayDraftError TooManyLabels(int count) =>
        new CreateOverlayDraftError.TooManyLabels(count);

    public static CreateOverlayDraftError FromViolation(LabelSetViolation violation, int count) =>
        violation switch
        {
            LabelSetViolation.Empty => EmptyLabelSet(),
            LabelSetViolation.TooMany => TooManyLabels(count),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown label-set violation."),
        };
}
