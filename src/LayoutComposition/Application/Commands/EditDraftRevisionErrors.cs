using System.Net;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Failure hierarchy for <see cref="EditDraftRevisionCommand"/>
/// (ADR-0047/0089). The four <c>LAYOUT_GRID_*</c> cases mirror the
/// aggregate's <see cref="GridViolation"/> set (ADR-0112 §2).
/// </summary>
public abstract record EditDraftRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNotFound(Guid Layout)
        : EditDraftRevisionError(
            "LAYOUT_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record LayoutRevisionNotFound(Guid Layout, int RevisionNumber)
        : EditDraftRevisionError(
            "LAYOUT_REVISION_NOT_FOUND",
            $"Layout {Layout} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record NotADraft(string FromState)
        : EditDraftRevisionError(
            "LAYOUT_REVISION_NOT_DRAFT",
            $"Revision is in state '{FromState}'; only Draft revisions can be edited in place.",
            HttpStatusCode.Conflict);

    public sealed record GridEmpty()
        : EditDraftRevisionError(
            "LAYOUT_GRID_EMPTY",
            "A layout revision must contain at least one tile.",
            HttpStatusCode.BadRequest);

    public sealed record TilePositionDuplicate()
        : EditDraftRevisionError(
            "LAYOUT_TILE_POSITION_DUPLICATE",
            "Two tiles occupy the same grid position.",
            HttpStatusCode.BadRequest);

    public sealed record TileOutOfBounds()
        : EditDraftRevisionError(
            "LAYOUT_TILE_OUT_OF_BOUNDS",
            "A tile sits outside the grid bounds.",
            HttpStatusCode.BadRequest);

    public sealed record GridTooLarge()
        : EditDraftRevisionError(
            "LAYOUT_GRID_TOO_LARGE",
            $"A grid may contain at most {GridDimensions.MaxTiles} tiles ({GridDimensions.MaxCells} cells).",
            HttpStatusCode.BadRequest);

    public static EditDraftRevisionError FromViolation(GridViolation violation) =>
        violation switch
        {
            GridViolation.Empty => new GridEmpty(),
            GridViolation.DuplicatePosition => new TilePositionDuplicate(),
            GridViolation.OutOfBounds => new TileOutOfBounds(),
            GridViolation.TooLarge => new GridTooLarge(),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown grid violation."),
        };

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record LayoutRevisionStale(Guid Layout, int ExpectedVersion, int ActualVersion)
        : EditDraftRevisionError(
            "LAYOUT_REVISION_STALE",
            $"Layout {Layout} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}
