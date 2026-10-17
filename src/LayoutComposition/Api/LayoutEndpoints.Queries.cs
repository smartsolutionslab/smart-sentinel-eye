using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Query (read) handlers for <see cref="LayoutEndpoints"/>.</summary>
public static partial class LayoutEndpoints
{
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> GetOne(
        Guid layoutIdentifier,
        HttpResponse response,
        [FromServices] GetLayoutQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
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
            .HandleAsync(new GetLayoutQuery(fabs, LayoutIdentifier.From(layoutIdentifier)), cancellationToken);

        return result.Match<IResult>(
            onSuccess: layout =>
            {
                // The version the caller must echo back in If-Match to mutate
                // this chain (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(layout.Version);

                return Results.Ok(layout);
            },
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListLayoutsQueryHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        LayoutRevisionState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                filter = LayoutRevisionState.From(state);
            }
            catch (ArgumentException)
            {
                return Results.Problem(
                    title: "LAYOUT_INVALID_STATE_FILTER",
                    detail: $"'{state}' is not a valid layout state (Draft | Published | Archived).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var fabsResolution =
            await LayoutCompositionFabResolution.ResolveReadFabsAsync(user, fabId, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result = await handler
            .HandleAsync(new ListLayoutsQuery(fabs, filter), cancellationToken);

        return result.Match<IResult>(
            onSuccess: payload => Results.Ok(new ListLayoutsResponse(payload.Chains, payload.Published)),
            onFailure: error => error.ToProblem());
    }
}
