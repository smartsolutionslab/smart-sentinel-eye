using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>Query (read) handlers for <see cref="OverlayEndpoints"/>.</summary>
public static partial class OverlayEndpoints
{
    private static async Task<IResult> GetOne(
        Guid overlayIdentifier,
        HttpResponse response,
        [FromServices] GetOverlayQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<OverlayDto, GetOverlayError> result = await handler
            .HandleAsync(new GetOverlayQuery(OverlayIdentifier.From(overlayIdentifier)), cancellationToken);

        return result.Match<IResult>(
            onSuccess: overlay =>
            {
                // The version the caller must echo back in If-Match to mutate
                // this chain (ADR-0113).
                response.Headers.ETag = ConcurrencyHeaders.ETag(overlay.Version);

                return Results.Ok(overlay);
            },
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> List(
        [FromQuery] string? state,
        [FromServices] ListOverlaysQueryHandler handler,
        CancellationToken cancellationToken)
    {
        OverlayRevisionState? filter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                filter = OverlayRevisionState.From(state);
            }
            catch (ArgumentException)
            {
                return Results.Problem(
                    title: "OVERLAY_INVALID_STATE_FILTER",
                    detail: $"'{state}' is not a valid overlay state (Draft | Published | Archived).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        Result<ListOverlaysResult, ListOverlaysError> result = await handler
            .HandleAsync(new ListOverlaysQuery(filter), cancellationToken);

        return result.Match<IResult>(
            onSuccess: payload => Results.Ok(new ListOverlaysResponse(payload.Chains, payload.Published)),
            onFailure: error => error.ToProblem());
    }
}
