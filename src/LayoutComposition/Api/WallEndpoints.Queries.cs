using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Query (read) handlers for <see cref="WallEndpoints"/>.</summary>
public static partial class WallEndpoints
{
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
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

        var fabsResolution =
            await LayoutCompositionFabResolution.ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler
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
        var fabsResolution =
            await LayoutCompositionFabResolution.ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var walls = await handler
            .HandleAsync(new ListWallsQuery(fabsResolution.Value), cancellationToken);

        return Results.Ok(walls);
    }
}
