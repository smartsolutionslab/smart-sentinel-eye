using System.Net;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="CreateWallCommand"/>
/// (ADR-0047 + ADR-0089), per plan.md §3's error table.
/// </summary>
public abstract record CreateWallError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record TooFewScenes(int Count)
        : CreateWallError(
            "WALL_TOO_FEW_SCENES",
            $"A wall needs at least {Wall.MinScenes} scenes; got {Count}.",
            HttpStatusCode.BadRequest);

    public sealed record TooManyScenes(int Count)
        : CreateWallError(
            "WALL_TOO_MANY_SCENES",
            $"A wall may have at most {Wall.MaxScenes} scenes; got {Count}.",
            HttpStatusCode.BadRequest);

    public sealed record DuplicateScene()
        : CreateWallError(
            "WALL_DUPLICATE_SCENE",
            "The scene set contains the same layout more than once.",
            HttpStatusCode.BadRequest);

    /// <summary>
    /// A candidate scene layout exists, but in a different fab than the wall
    /// (US1-10). Distinct from <see cref="SceneNotFound"/> so the caller's
    /// own fab is checked without disclosing anything about the other one.
    /// </summary>
    public sealed record SceneOtherFab(Guid Layout)
        : CreateWallError(
            "WALL_SCENE_OTHER_FAB",
            $"Layout {Layout} does not belong to this wall's fab.",
            HttpStatusCode.BadRequest);

    public sealed record SceneNotFound(Guid Layout)
        : CreateWallError(
            "WALL_SCENE_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.BadRequest);

    /// <summary>PD-6: every scene must be Published at create time.</summary>
    public sealed record SceneNotPublished(Guid Layout)
        : CreateWallError(
            "WALL_SCENE_NOT_PUBLISHED",
            $"Layout {Layout} has no Published revision.",
            HttpStatusCode.Conflict);

    public sealed record NameTaken(string Name)
        : CreateWallError(
            "WALL_NAME_TAKEN",
            $"A live wall named '{Name}' already exists in this fab.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="CreateWallError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class CreateWallFailures
{
    public static CreateWallError TooFewScenes(int count) => new CreateWallError.TooFewScenes(count);

    public static CreateWallError TooManyScenes(int count) => new CreateWallError.TooManyScenes(count);

    public static CreateWallError DuplicateScene() => new CreateWallError.DuplicateScene();

    public static CreateWallError SceneOtherFab(Guid layout) => new CreateWallError.SceneOtherFab(layout);

    public static CreateWallError SceneNotFound(Guid layout) => new CreateWallError.SceneNotFound(layout);

    public static CreateWallError SceneNotPublished(Guid layout) => new CreateWallError.SceneNotPublished(layout);

    public static CreateWallError NameTaken(string name) => new CreateWallError.NameTaken(name);

    public static CreateWallError FromViolation(SceneSetViolation violation, int count) =>
        violation switch
        {
            SceneSetViolation.TooFew => TooFewScenes(count),
            SceneSetViolation.TooMany => TooManyScenes(count),
            SceneSetViolation.Duplicate => DuplicateScene(),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown scene-set violation."),
        };
}
