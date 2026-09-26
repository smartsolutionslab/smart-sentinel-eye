using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="SwitchWallSceneCommand"/>
/// (ADR-0047 + ADR-0089).
/// </summary>
public abstract record SwitchWallSceneError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record WallNotFound(Guid Wall)
        : SwitchWallSceneError(
            "WALL_NOT_FOUND",
            $"Wall {Wall} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record Stale(Guid Wall, int ExpectedVersion, int ActualVersion)
        : SwitchWallSceneError(
            "WALL_STALE",
            $"Wall {Wall} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);

    /// <summary>US1-11: the target names a layout that is not among the wall's scenes.</summary>
    public sealed record SceneNotInSet(Guid Layout)
        : SwitchWallSceneError(
            "WALL_SCENE_NOT_IN_SET",
            $"Layout {Layout} is not one of this wall's scenes.",
            HttpStatusCode.BadRequest);

    /// <summary>US1-8, PD-6: the target scene's layout has no Published revision.</summary>
    public sealed record SceneNotPublished(Guid Layout)
        : SwitchWallSceneError(
            "WALL_SCENE_NOT_PUBLISHED",
            $"Layout {Layout} has no Published revision.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="SwitchWallSceneError"/> as the base rather than the
/// variant (ADR-0047).
/// </summary>
public static class SwitchWallSceneFailures
{
    public static SwitchWallSceneError WallNotFound(Guid wall) => new SwitchWallSceneError.WallNotFound(wall);

    public static SwitchWallSceneError Stale(Guid wall, int expectedVersion, int actualVersion) =>
        new SwitchWallSceneError.Stale(wall, expectedVersion, actualVersion);

    public static SwitchWallSceneError SceneNotInSet(Guid layout) => new SwitchWallSceneError.SceneNotInSet(layout);

    public static SwitchWallSceneError SceneNotPublished(Guid layout) => new SwitchWallSceneError.SceneNotPublished(layout);
}
