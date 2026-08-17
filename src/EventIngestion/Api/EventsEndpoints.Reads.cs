using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Read-API handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    private static async Task<IResult> ListEvents(
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        // Optional since spec 018 (FR-003): omitting it spans every fab the
        // caller holds. It was required, which is why a leak looked like
        // scoping — the caller had to name a fab and nothing checked they held
        // it.
        [FromQuery] string? fabId,
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
        Source? sourceVo = null;
        DeviceIdentifier? deviceVo = null;
        Kind? kindVo = null;
        try
        {
            if (!string.IsNullOrEmpty(source))
            {
                sourceVo = Source.From(source);
            }

            if (!string.IsNullOrEmpty(deviceId))
            {
                deviceVo = DeviceIdentifier.From(deviceId);
            }

            if (!string.IsNullOrEmpty(kind))
            {
                kindVo = Kind.From(kind);
            }
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_LIST_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        Result<EventPageDto, ListEventsError> result = await handler.HandleAsync(
            new ListEventsQuery(
                fabs, sourceVo, deviceVo, kindVo,
                occurredAfter, occurredBefore, ingestedAfter, ingestedBefore,
                pageSize ?? 100, cursor),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetEvent(
        Guid eventId,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] GetEventQueryHandler handler,
        CancellationToken cancellationToken)
    {
        EventIdentifier identifier;
        try
        {
            identifier = EventIdentifier.From(eventId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        (IReadOnlyList<FabIdentifier>? fabs, IResult? fabProblem) =
            await ResolveReadFabsAsync(user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabs is null)
        {
            return fabProblem!;
        }

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(fabs, identifier), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> ListDeadLetters(
        [FromQuery] int? limit,
        [FromServices] ListDeadLettersQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(
                new ListDeadLettersQuery(limit ?? 100), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }
}
