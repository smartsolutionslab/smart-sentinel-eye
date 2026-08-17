using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Write-path handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    private const string BearerPrefix = "Bearer ";

    private static async Task<IResult> IngestManual(
        [FromBody] IngestManualEventRequest body,
        [FromServices] IIngestChannel channel,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        // Resolved from the caller, never from the request (spec 018 FR-006).
        // Before this, `fabId` went straight into the envelope unchecked, so an
        // operator could file an event against any plant — where it drives that
        // plant's automation rules and changes what its operators see.
        //
        // Resolved BEFORE the channel is touched (FR-007): a refusal that had
        // already enqueued would place a fabricated event in another plant's
        // stream while reporting that it had been stopped.
        (FabIdentifier? fab, IResult? fabProblem) =
            await ResolveWriteFabAsync(user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fab is null)
        {
            return fabProblem!;
        }

        EventEnvelope envelope;
        try
        {
            envelope = new EventEnvelope(
                EventIdentifier.New(),
                fab,
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
        Ensure.That(body).IsNotNull();
        Ensure.That(request).IsNotNull();

        WebhookIntegration? integration = await AuthenticateWebhookAsync(
            integrationName, request, fabId, integrations, cancellationToken);
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
            .GetByNameAsync(parsedName, cancellationToken);
        if (!found.HasValue || found.Value.IsRevoked)
        {
            return null;
        }

        WebhookIntegration integration = found.Value;
        bool authorized = integration.ValidationMode == BearerValidationMode.Jwt
            ? await ValidateJwtAsync(request, integration, fabId)
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
            .AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
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
