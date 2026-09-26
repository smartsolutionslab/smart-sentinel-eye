using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

public abstract record GetWallError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record WallNotFound(Guid Wall)
        : GetWallError(
            "WALL_NOT_FOUND",
            $"Wall {Wall} does not exist.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="GetWallError"/> as the base rather than the variant
/// (ADR-0047).
/// </summary>
public static class GetWallFailures
{
    public static GetWallError WallNotFound(Guid wall) => new GetWallError.WallNotFound(wall);
}
