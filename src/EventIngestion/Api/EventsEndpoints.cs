using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder writes = app.MapGroup("/events").WithTags("Events");

        // 403 became reachable with spec 018: the caller may name a fab they do
        // not hold, or hold none at all. 400 was already declared but now
        // covers a second cause — a multi-fab operator omitting fabId. Spec 013
        // shipped this wrong on one endpoint and it took a review to catch.
        writes.MapPost("/manual", IngestManual)
            .RequireAuthorization(Scope.Sse.Events.Write)
            .WithName("IngestManualEvent")
            .WithSummary(
                "Ingest an event into the resolved fab. Omit fabId when you belong to exactly one; "
                + "name it when you belong to several (ADR-0114). The fab is never taken from the "
                + "request unchecked (spec 018 FR-006).")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/{eventId:guid}", GetEvent)
            .WithName("GetEvent")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/dead-letters", ListDeadLetters)
            .WithName("ListDeadLetters")
            .WithSummary(
                "List rejected deliveries from the fabs you hold. A delivery whose plant could "
                + "not be established from its address is returned to nobody (spec 018 FR-011).")
            .Produces<IReadOnlyList<DeadLetterDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
