using System.Net;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="CreateLayoutDraftCommand"/> (ADR-0047 + ADR-0089). Each
/// case carries Code, Message, and HttpStatusCode so the API layer maps
/// to RFC 7807 Problem Details without per-case translation. The four
/// <c>LAYOUT_GRID_*</c> cases mirror the aggregate's
/// <see cref="GridViolation"/> set (ADR-0112 §2).
/// </summary>
public abstract record CreateLayoutDraftError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNameTaken(string Name)
        : CreateLayoutDraftError(
            "LAYOUT_NAME_TAKEN",
            $"A non-archived layout with the name '{Name}' already exists.",
            HttpStatusCode.Conflict);

    public sealed record GridEmpty()
        : CreateLayoutDraftError(
            "LAYOUT_GRID_EMPTY",
            "A layout revision must contain at least one tile.",
            HttpStatusCode.BadRequest);

    public sealed record TilePositionDuplicate()
        : CreateLayoutDraftError(
            "LAYOUT_TILE_POSITION_DUPLICATE",
            "Two tiles occupy the same grid position.",
            HttpStatusCode.BadRequest);

    public sealed record TileOutOfBounds()
        : CreateLayoutDraftError(
            "LAYOUT_TILE_OUT_OF_BOUNDS",
            "A tile sits outside the grid bounds.",
            HttpStatusCode.BadRequest);

    public sealed record GridTooLarge()
        : CreateLayoutDraftError(
            "LAYOUT_GRID_TOO_LARGE",
            $"A grid may contain at most {GridDimensions.MaxTiles} tiles ({GridDimensions.MaxCells} cells).",
            HttpStatusCode.BadRequest);

    public static CreateLayoutDraftError FromViolation(GridViolation violation) =>
        violation switch
        {
            GridViolation.Empty => new GridEmpty(),
            GridViolation.DuplicatePosition => new TilePositionDuplicate(),
            GridViolation.OutOfBounds => new TileOutOfBounds(),
            GridViolation.TooLarge => new GridTooLarge(),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown grid violation."),
        };
}
