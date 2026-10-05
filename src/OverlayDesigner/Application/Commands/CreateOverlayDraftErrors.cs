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
    public sealed record EmptyElementSet()
        : CreateOverlayDraftError(
            "OVERLAY_ELEMENTS_EMPTY",
            "A revision must carry at least one element.",
            HttpStatusCode.BadRequest);

    /// <summary>Spec 150 FR-002, ADR-0164 — the label set exceeds the ceiling.</summary>
    public sealed record TooManyElements(int Count)
        : CreateOverlayDraftError(
            "OVERLAY_ELEMENTS_TOO_MANY",
            $"A revision may carry at most {OverlayElement.MaxElements} elements; {Count} were submitted.",
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

    public static CreateOverlayDraftError EmptyElementSet() =>
        new CreateOverlayDraftError.EmptyElementSet();

    public static CreateOverlayDraftError TooManyElements(int count) =>
        new CreateOverlayDraftError.TooManyElements(count);

    public static CreateOverlayDraftError FromViolation(ElementSetViolation violation, int count) =>
        violation switch
        {
            ElementSetViolation.Empty => EmptyElementSet(),
            ElementSetViolation.TooMany => TooManyElements(count),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown element-set violation."),
        };
}
