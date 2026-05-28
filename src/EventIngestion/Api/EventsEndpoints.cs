using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Minimal-API endpoint group for EventIngestion (ADR-0070).
/// Spec 006 US3 — manual operator annotations.
///
/// <para>
/// HTTP ingress also funnels through the bounded
/// <see cref="IIngestChannel"/> so the same backpressure rules
/// apply: a full channel makes the endpoint return 429.
/// Webhook + read endpoints land in PR E.
/// </para>
/// </summary>
public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/events")
            .RequireAuthorization()
            .WithTags("Events");

        group.MapPost("/manual", IngestManual)
            .RequireAuthorization(AuthenticationDefaults.AdminPolicy)
            .WithName("IngestManualEvent")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> IngestManual(
        [FromBody] IngestManualEventRequest body,
        [FromQuery] string fabId,
        [FromServices] IIngestChannel channel,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        EventEnvelope envelope;
        try
        {
            envelope = new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From(fabId),
                Source.Manual,
                DeviceIdentifier.From(body.DeviceId),
                Kind.From(body.Kind),
                OccurredAt.From(body.OccurredAt),
                Payload.From(body.Payload.GetRawText()));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!channel.TryWrite(envelope))
        {
            return Results.Problem(
                title: "EVENT_INGEST_BACKPRESSURE",
                detail: "Event ingestion channel is full; please retry.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        _ = cancellationToken; // accepted asynchronously; persistence loop owns the actual write
        _ = clock;

        return Results.Accepted(value: envelope.Identifier.Value);
    }
}
