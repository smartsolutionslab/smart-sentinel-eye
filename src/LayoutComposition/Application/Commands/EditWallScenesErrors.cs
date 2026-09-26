using System.Net;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="EditWallScenesCommand"/>
/// (ADR-0047 + ADR-0089).
/// </summary>
public abstract record EditWallScenesError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record WallNotFound(Guid Wall)
        : EditWallScenesError(
            "WALL_NOT_FOUND",
            $"Wall {Wall} does not exist.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// ADR-0113 Layer 1: the caller's view of the wall has since moved.
    /// 409, not 412, consistent with <c>LAYOUT_REVISION_STALE</c>.
    /// </summary>
    public sealed record Stale(Guid Wall, int ExpectedVersion, int ActualVersion)
        : EditWallScenesError(
            "WALL_STALE",
            $"Wall {Wall} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);

    public sealed record TooFewScenes(int Count)
        : EditWallScenesError(
            "WALL_TOO_FEW_SCENES",
            $"A wall needs at least {Wall.MinScenes} scenes; got {Count}.",
            HttpStatusCode.BadRequest);

    public sealed record TooManyScenes(int Count)
        : EditWallScenesError(
            "WALL_TOO_MANY_SCENES",
            $"A wall may have at most {Wall.MaxScenes} scenes; got {Count}.",
            HttpStatusCode.BadRequest);

    public sealed record DuplicateScene()
        : EditWallScenesError(
            "WALL_DUPLICATE_SCENE",
            "The scene set contains the same layout more than once.",
            HttpStatusCode.BadRequest);

    /// <summary>
    /// Also returned for a layout that exists but in a different fab than
    /// the wall — that must not be distinguishable from one that doesn't
    /// exist anywhere, or the response discloses the identifier exists
    /// somewhere.
    /// </summary>
    public sealed record SceneNotFound(Guid Layout)
        : EditWallScenesError(
            "WALL_SCENE_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.BadRequest);

    public sealed record SceneNotPublished(Guid Layout)
        : EditWallScenesError(
            "WALL_SCENE_NOT_PUBLISHED",
            $"Layout {Layout} has no Published revision.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds an <see cref="EditWallScenesError"/> as the base rather than the
/// variant (ADR-0047).
/// </summary>
public static class EditWallScenesFailures
{
    public static EditWallScenesError WallNotFound(Guid wall) => new EditWallScenesError.WallNotFound(wall);

    public static EditWallScenesError Stale(Guid wall, int expectedVersion, int actualVersion) =>
        new EditWallScenesError.Stale(wall, expectedVersion, actualVersion);

    public static EditWallScenesError TooFewScenes(int count) => new EditWallScenesError.TooFewScenes(count);

    public static EditWallScenesError TooManyScenes(int count) => new EditWallScenesError.TooManyScenes(count);

    public static EditWallScenesError DuplicateScene() => new EditWallScenesError.DuplicateScene();

    public static EditWallScenesError SceneNotFound(Guid layout) => new EditWallScenesError.SceneNotFound(layout);

    public static EditWallScenesError SceneNotPublished(Guid layout) => new EditWallScenesError.SceneNotPublished(layout);

    public static EditWallScenesError FromViolation(SceneSetViolation violation, int count) =>
        violation switch
        {
            SceneSetViolation.TooFew => TooFewScenes(count),
            SceneSetViolation.TooMany => TooManyScenes(count),
            SceneSetViolation.Duplicate => DuplicateScene(),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown scene-set violation."),
        };
}
