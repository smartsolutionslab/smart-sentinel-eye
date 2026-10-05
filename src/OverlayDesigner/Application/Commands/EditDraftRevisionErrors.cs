using System.Net;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

public abstract record EditDraftRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : EditDraftRevisionError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record OverlayRevisionNotFound(Guid Overlay, int RevisionNumber)
        : EditDraftRevisionError(
            "OVERLAY_REVISION_NOT_FOUND",
            $"Overlay {Overlay} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record NotADraft(string FromState)
        : EditDraftRevisionError(
            "OVERLAY_REVISION_NOT_DRAFT",
            $"Revision is in state '{FromState}'; only Draft revisions can be edited in place.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record OverlayRevisionStale(Guid Overlay, int ExpectedVersion, int ActualVersion)
        : EditDraftRevisionError(
            "OVERLAY_REVISION_STALE",
            $"Overlay {Overlay} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);

    /// <summary>Spec 150 FR-003 — a revision must carry at least one label.</summary>
    public sealed record EmptyElementSet()
        : EditDraftRevisionError(
            "OVERLAY_ELEMENTS_EMPTY",
            "A revision must carry at least one element.",
            HttpStatusCode.BadRequest);

    /// <summary>Spec 150 FR-002, ADR-0164 — the label set exceeds the ceiling.</summary>
    public sealed record TooManyElements(int Count)
        : EditDraftRevisionError(
            "OVERLAY_ELEMENTS_TOO_MANY",
            $"A revision may carry at most {OverlayElement.MaxElements} elements; {Count} were submitted.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="EditDraftRevisionError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class EditDraftRevisionFailures
{
    public static EditDraftRevisionError OverlayNotFound(Guid overlay) =>
        new EditDraftRevisionError.OverlayNotFound(overlay);

    public static EditDraftRevisionError OverlayRevisionNotFound(Guid overlay, int revisionNumber) =>
        new EditDraftRevisionError.OverlayRevisionNotFound(overlay, revisionNumber);

    public static EditDraftRevisionError NotADraft(string fromState) =>
        new EditDraftRevisionError.NotADraft(fromState);

    public static EditDraftRevisionError OverlayRevisionStale(Guid overlay, int expectedVersion, int actualVersion) =>
        new EditDraftRevisionError.OverlayRevisionStale(overlay, expectedVersion, actualVersion);

    public static EditDraftRevisionError EmptyElementSet() =>
        new EditDraftRevisionError.EmptyElementSet();

    public static EditDraftRevisionError TooManyElements(int count) =>
        new EditDraftRevisionError.TooManyElements(count);

    public static EditDraftRevisionError FromViolation(ElementSetViolation violation, int count) =>
        violation switch
        {
            ElementSetViolation.Empty => EmptyElementSet(),
            ElementSetViolation.TooMany => TooManyElements(count),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown element-set violation."),
        };
}
