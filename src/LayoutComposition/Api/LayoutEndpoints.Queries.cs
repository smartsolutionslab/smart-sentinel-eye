using Microsoft.AspNetCore.Http;
using SmartSentinelEye.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Query (read) handlers for <see cref="LayoutEndpoints"/>.</summary>
public static partial class LayoutEndpoints
{
    private static async Task<IResult> GetOne(
        Guid layoutIdentifier,
        [FromServices] GetLayoutQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (layoutIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "LAYOUT_INVALID_INPUT",
                detail: "layoutIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<LayoutDto, GetLayoutError> result = await handler
            .HandleAsync(new GetLayoutQuery(LayoutIdentifier.From(layoutIdentifier)), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListLayoutsQueryHandler handler,
        CancellationToken cancellationToken)
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

        Result<ListLayoutsResult, ListLayoutsError> result = await handler
            .HandleAsync(new ListLayoutsQuery(filter), cancellationToken)
            .ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: payload => Results.Ok(new ListLayoutsResponse(payload.Chains, payload.Published)),
            onFailure: error => error.ToProblem());
    }
}
