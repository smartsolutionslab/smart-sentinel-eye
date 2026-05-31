using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Minimal-API endpoint group for EventIngestion (ADR-0070): the manual
/// and webhook write paths plus the read API. Handlers are split across
/// partial files — <c>EventsEndpoints.Writes.cs</c> and
/// <c>EventsEndpoints.Reads.cs</c>.
/// </summary>
public static partial class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder writes = app.MapGroup("/events").WithTags("Events");

        writes.MapPost("/manual", IngestManual)
            .RequireAuthorization(Scope.Sse.Events.Write)
            .WithName("IngestManualEvent")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        writes.MapPost("/webhook/{integrationName}", IngestWebhook)
            .AllowAnonymous() // auth is the static bearer token, not OIDC
            .WithName("IngestWebhookEvent")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        RouteGroupBuilder reads = app.MapGroup("/events")
            .RequireAuthorization(Scope.Sse.Events.Read)
            .WithTags("EventsRead");

        reads.MapGet("/", ListEvents)
            .WithName("ListEvents")
            .Produces<EventPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        reads.MapGet("/{eventId:guid}", GetEvent)
            .WithName("GetEvent")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reads.MapGet("/dead-letters", ListDeadLetters)
            .WithName("ListDeadLetters")
            .Produces<IReadOnlyList<DeadLetterDto>>(StatusCodes.Status200OK);

        return app;
    }
}
