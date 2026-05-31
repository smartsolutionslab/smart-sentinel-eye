using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Write-path handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    private const string BearerPrefix = "Bearer ";

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

        WebhookIntegration? integration = await AuthenticateWebhookAsync(
            integrationName, request, fabId, integrations, cancellationToken).ConfigureAwait(false);
        if (integration is null)
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
    /// Authenticates a webhook caller and returns the matching integration,
    /// or <c>null</c> if the bearer token is missing/malformed, the
    /// integration is unknown or revoked, or the token fails validation.
    /// Every failure path collapses to <c>null</c> so the 401 response never
    /// leaks which integrations exist.
    /// </summary>
    private static async Task<WebhookIntegration?> AuthenticateWebhookAsync(
        string integrationName,
        HttpRequest request,
        string fabId,
        IWebhookIntegrationRepository integrations,
        CancellationToken cancellationToken)
    {
        string? authHeader = request.Headers.Authorization;
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        string token = authHeader[BearerPrefix.Length..];

        WebhookIntegrationName parsedName;
        try
        {
            parsedName = WebhookIntegrationName.From(integrationName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        Option<WebhookIntegration> found = await integrations
            .GetByNameAsync(parsedName, cancellationToken).ConfigureAwait(false);
        if (!found.HasValue || found.Value.IsRevoked)
        {
            return null;
        }

        WebhookIntegration integration = found.Value;
        bool authorized = integration.ValidationMode == BearerValidationMode.Jwt
            ? await ValidateJwtAsync(request, integration, fabId).ConfigureAwait(false)
            : integration.TokenHash.Matches(token);
        return authorized ? integration : null;
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

    private static IResult EnqueueOrBackpressure(IIngestChannel channel, EventEnvelope envelope) =>
        channel.TryWrite(envelope)
            ? Results.Accepted(value: envelope.Identifier.Value)
            : Results.Problem(
                title: "EVENT_INGEST_BACKPRESSURE",
                detail: "Event ingestion channel is full; please retry.",
                statusCode: StatusCodes.Status429TooManyRequests);
}
