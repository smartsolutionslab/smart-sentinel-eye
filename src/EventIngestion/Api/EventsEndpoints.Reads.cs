using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Read-API handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    private static async Task<IResult> ListEvents(
        [FromQuery] string fabId,
        [FromQuery] string? source,
        [FromQuery] string? deviceId,
        [FromQuery] string? kind,
        [FromQuery] DateTimeOffset? occurredAfter,
        [FromQuery] DateTimeOffset? occurredBefore,
        [FromQuery] DateTimeOffset? ingestedAfter,
        [FromQuery] DateTimeOffset? ingestedBefore,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ListEventsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        FabIdentifier fab;
        Source? sourceVo = null;
        DeviceIdentifier? deviceVo = null;
        Kind? kindVo = null;
        try
        {
            fab = FabIdentifier.From(fabId);
            if (!string.IsNullOrEmpty(source)) sourceVo = Source.From(source);
            if (!string.IsNullOrEmpty(deviceId)) deviceVo = DeviceIdentifier.From(deviceId);
            if (!string.IsNullOrEmpty(kind)) kindVo = Kind.From(kind);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_LIST_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<EventPageDto, ListEventsError> result = await handler.HandleAsync(
            new ListEventsQuery(
                fab, sourceVo, deviceVo, kindVo,
                occurredAfter, occurredBefore, ingestedAfter, ingestedBefore,
                pageSize ?? 100, cursor),
            cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> GetEvent(
        Guid eventId,
        [FromQuery] string fabId,
        [FromServices] GetEventQueryHandler handler,
        CancellationToken cancellationToken)
    {
        FabIdentifier fab;
        EventIdentifier identifier;
        try
        {
            fab = FabIdentifier.From(fabId);
            identifier = EventIdentifier.From(eventId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(fab, identifier), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }

    private static async Task<IResult> ListDeadLetters(
        [FromQuery] int? limit,
        [FromServices] ListDeadLettersQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(
                new ListDeadLettersQuery(limit ?? 100), cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => Results.Problem(
                title: error.Code, detail: error.Message, statusCode: (int)error.Status));
    }
}
