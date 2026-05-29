using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Minimal-API endpoint group for EventIngestion (ADR-0070).
/// Spec 006 US3 (manual) + US4 (webhook) write paths plus the
/// read API.
/// </summary>
public static class EventsEndpoints
{
    private const string BearerPrefix = "Bearer ";

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

    private static IResult IngestManual(
        [FromBody] IngestManualEventRequest body,
        [FromQuery] string fabId,
        [FromServices] IIngestChannel channel)
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
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return EnqueueOrBackpressure(channel, envelope);
    }

    private static async Task<IResult> IngestWebhook(
        string integrationName,
        [FromBody] IngestWebhookEventRequest body,
        [FromQuery] string fabId,
        HttpRequest request,
        [FromServices] IIngestChannel channel,
        [FromServices] IWebhookIntegrationRepository integrations,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(request);

        string? authHeader = request.Headers.Authorization;
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }
        string token = authHeader[BearerPrefix.Length..];

        WebhookIntegrationName parsedName;
        try
        {
            parsedName = WebhookIntegrationName.From(integrationName);
        }
        catch (ArgumentException)
        {
            return Results.Unauthorized();
        }

        Option<WebhookIntegration> found = await integrations
            .GetByNameAsync(parsedName, cancellationToken).ConfigureAwait(false);

        // Lookup miss / revoked share the static 401 so the
        // response never leaks which integrations exist.
        if (!found.HasValue || found.Value.IsRevoked)
        {
            return Results.Unauthorized();
        }

        WebhookIntegration integration = found.Value;
        bool authorized = integration.ValidationMode == BearerValidationMode.Jwt
            ? await ValidateJwtAsync(request, integration, fabId).ConfigureAwait(false)
            : integration.TokenHash.Matches(token);
        if (!authorized)
        {
            return Results.Unauthorized();
        }

        EventEnvelope envelope;
        try
        {
            envelope = new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From(fabId),
                Source.Webhook,
                DeviceIdentifier.From(integration.Name.Value),
                Kind.From(body.Kind ?? integration.DefaultKind.Value),
                OccurredAt.From(body.OccurredAt ?? clock.UtcNow),
                Payload.From(body.Payload.GetRawText()));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return EnqueueOrBackpressure(channel, envelope);
    }

    /// <summary>
    /// Validates a Keycloak-minted JWT against the integration's
    /// rotated <c>KeycloakClientId</c> (spec 008 FR-016). The
    /// caller is authorised iff: signature + expiry valid, scope
    /// contains <c>sse.events.write</c>, azp matches the
    /// integration's clientId, and groups contains
    /// <c>/fabs/&lt;fabId&gt;</c>.
    /// </summary>
    private static async Task<bool> ValidateJwtAsync(
        HttpRequest request, WebhookIntegration integration, string fabId)
    {
        AuthenticateResult auth = await request.HttpContext
            .AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!auth.Succeeded || auth.Principal is null)
        {
            return false;
        }

        ClaimsPrincipal user = auth.Principal;

        bool hasEventsWriteScope = user.FindAll("scope").Any(claim =>
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("sse.events.write", StringComparer.Ordinal));
        if (!hasEventsWriteScope)
        {
            return false;
        }

        string? azp = user.FindFirst("azp")?.Value;
        if (!string.Equals(azp, integration.KeycloakClientId, StringComparison.Ordinal))
        {
            return false;
        }

        string targetGroup = "/fabs/" + fabId;
        return user.FindAll("groups").Any(claim =>
            claim.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Contains(targetGroup, StringComparer.Ordinal));
    }

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

    private static IResult EnqueueOrBackpressure(IIngestChannel channel, EventEnvelope envelope) =>
        channel.TryWrite(envelope)
            ? Results.Accepted(value: envelope.Identifier.Value)
            : Results.Problem(
                title: "EVENT_INGEST_BACKPRESSURE",
                detail: "Event ingestion channel is full; please retry.",
                statusCode: StatusCodes.Status429TooManyRequests);
}
