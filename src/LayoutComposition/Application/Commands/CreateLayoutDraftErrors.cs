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

    /// <summary>
    /// Spec 017 FR-014 / FR-015. Names the offending cameras, because a layout
    /// may hold four tiles and "one of them is wrong" is not actionable.
    ///
    /// <para>
    /// The message deliberately does not say <em>which</em> fab an offending
    /// camera is in, nor whether it exists at all: the caller cannot see that
    /// fab's cameras, and saying so would turn this refusal into the
    /// enumeration oracle FR-006 exists to close.
    /// </para>
    /// </summary>
    public sealed record TileCameraOutsideFab(string Fab, IReadOnlyList<Guid> Cameras)
        : CreateLayoutDraftError(
            "LAYOUT_TILE_CAMERA_OUTSIDE_FAB",
            $"These cameras are not available in fab '{Fab}': {string.Join(", ", Cameras)}.",
            HttpStatusCode.BadRequest);

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

/// <summary>
/// Builds a <see cref="CreateLayoutDraftError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class CreateLayoutDraftFailures
{
    public static CreateLayoutDraftError LayoutNameTaken(string name) =>
        new CreateLayoutDraftError.LayoutNameTaken(name);

    public static CreateLayoutDraftError TileCameraOutsideFab(string fab, IReadOnlyList<Guid> cameras) =>
        new CreateLayoutDraftError.TileCameraOutsideFab(fab, cameras);

    public static CreateLayoutDraftError GridEmpty() =>
        new CreateLayoutDraftError.GridEmpty();

    public static CreateLayoutDraftError TilePositionDuplicate() =>
        new CreateLayoutDraftError.TilePositionDuplicate();

    public static CreateLayoutDraftError TileOutOfBounds() =>
        new CreateLayoutDraftError.TileOutOfBounds();

    public static CreateLayoutDraftError GridTooLarge() =>
        new CreateLayoutDraftError.GridTooLarge();
}
