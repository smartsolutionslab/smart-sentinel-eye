using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Query (read) handlers for <see cref="WallEndpoints"/>.</summary>
public static partial class WallEndpoints
{
    private static async Task<IResult> GetOne(
        Guid wallIdentifier,
        HttpResponse response,
        [FromServices] GetWallQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (wallIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "WALL_INVALID_INPUT",
                detail: "wallIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await LayoutEndpoints.ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<FabIdentifier> fabs = fabsResolution.Value;

        Result<WallDto, GetWallError> result = await handler
            .HandleAsync(new GetWallQuery(fabs, WallIdentifier.From(wallIdentifier)), cancellationToken);

        return result.Match<IResult>(
            onSuccess: wall =>
            {
                // The version the caller must echo back in If-Match to mutate
                // this wall (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(wall.Version);

                return Results.Ok(wall);
            },
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromServices] ListWallsQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await LayoutEndpoints.ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        IReadOnlyList<WallDto> walls = await handler
            .HandleAsync(new ListWallsQuery(fabsResolution.Value), cancellationToken);

        return Results.Ok(walls);
    }
}
