using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Write-path handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string IngestManualEndpoint = "POST /events/manual";

    private static async Task<IResult> IngestManual(
        [FromBody] IngestManualEventRequest body,
        [AsParameters] IngestManualServices services,
        HttpContext http,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        var admission = await AdmitManualCallerAsync(user, fabId, services, cancellationToken);
        if (admission.IsFailure)
        {
            return admission.Error;
        }

        EventEnvelope envelope;
        try
        {
            envelope = ManualEnvelope(admission.Value, body);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The envelope already carries a fresh EventIdentifier, so a retry without
        // a key would file a second event under a second identifier — no conflict
        // to notice, just a duplicate. With a key the caller gets the first
        // event's identifier back instead.
        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                ManualScope(key, user, admission.Value, body),
                services.Idempotency,
                services.Clock),
            identifier => $"/events/{identifier}",
            token => EventIngestStore.StoreOrRefuseAsync(services.Limiter, services.ScopeFactory, envelope, token),
            cancellationToken);
    }

    private static async Task<Result<FabIdentifier, IResult>> AdmitManualCallerAsync(
        ClaimsPrincipal user,
        string? fabId,
        IngestManualServices services,
        CancellationToken cancellationToken)
    {
        // ADR-0160's generic DisabledAt check only trusts a snapshot that is
        // stamped *after* Keycloak successfully disables the client (spec 264,
        // "Keycloak first, then the row") — a window #2629 widened to ~70 min.
        // This integration's own RevokedAt is stamped at the revoke decision
        // itself, the same moment /events/webhook/{name}'s check reads it
        // (#2814), so a webhook-sourced caller is refused here just as fast.
        if (await IsRevokedWebhookCallerAsync(user, services.WebhookIntegrations, cancellationToken))
        {
            return Result<FabIdentifier, IResult>.Failure(Results.Unauthorized());
        }

        // Resolved from the caller, never from the request (spec 018 FR-006).
        // Before this, `fabId` went straight into the envelope unchecked, so an
        // operator could file an event against any plant — where it drives that
        // plant's automation rules and changes what its operators see.
        //
        // Resolved BEFORE anything is written (spec 018 FR-007): a refusal that
        // had already stored would place a fabricated event in another plant's
        // stream while reporting that it had been stopped.
        var fabResolution =
            await EventIngestionFabResolution.ResolveWriteFabAsync(
                user, fabId ?? string.Empty, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution;
        }

        var fab = fabResolution.Value;

        // Spec 019 FR-007. Still checked first: it names the cause precisely
        // ("this plant has no storage") where the write below could only report
        // that something failed. Since spec 020 the write is synchronous, so
        // neither answer can be given after the event was already accepted.
        if (!await services.Storage.IsReadyAsync(fab, cancellationToken))
        {
            return Result<FabIdentifier, IResult>.Failure(FabNotProvisioned(fab));
        }

        return fabResolution;
    }

    private static EventEnvelope ManualEnvelope(FabIdentifier fab, IngestManualEventRequest body) =>
        new(
            EventIdentifier.New(),
            fab,
            Source.Manual,
            DeviceIdentifier.From(body.DeviceId),
            Kind.From(body.Kind),
            OccurredAt.From(body.OccurredAt),
            Payload.From(body.Payload));

    private static Option<IdempotencyScope> ManualScope(
        Option<IdempotencyKey> key,
        ClaimsPrincipal user,
        FabIdentifier fab,
        IngestManualEventRequest body) =>
        key.Map(supplied => IdempotencyScope.For(
            supplied,
            IngestManualEndpoint,
            user.ToOperatorIdentifier().Value.ToString(),
            Option<string>.Some(fab.Value),
            IdempotencyFingerprint.Of(body)));

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084); idempotency added two more collaborators.
    /// </summary>
    private sealed record IngestManualServices(
        [FromServices] IngestWriteLimiter Limiter,
        [FromServices] IServiceScopeFactory ScopeFactory,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IFabStorageReadiness Storage,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock,
        [FromServices] IWebhookIntegrationRepository WebhookIntegrations);

    /// <summary>
    /// A manual caller presenting a rotated webhook integration's own JWT
    /// (identified by <c>azp</c>) is refused once that integration is
    /// revoked, mirroring <c>AuthenticateWebhookAsync</c>'s check (#2814). A
    /// caller with no <c>azp</c>, or one no rotated integration claims, is
    /// unaffected — this only narrows an already-authenticated webhook-sourced
    /// token, never a human operator's.
    /// </summary>
    private static async Task<bool> IsRevokedWebhookCallerAsync(
        ClaimsPrincipal user, IWebhookIntegrationRepository webhookIntegrations, CancellationToken cancellationToken)
    {
        var azp = user.FindFirst("azp")?.Value;
        if (string.IsNullOrWhiteSpace(azp) || azp.Length > KeycloakClientIdentifier.MaximumLength)
        {
            return false;
        }

        return await webhookIntegrations.IsRevokedByKeycloakClientIdAsync(
            KeycloakClientIdentifier.From(azp), cancellationToken);
    }

    private static async Task<IResult> IngestWebhook(
        string integrationName,
        [FromBody] IngestWebhookEventRequest body,
        [FromQuery] string fabId,
        HttpRequest request,
        [FromServices] IngestWriteLimiter limiter,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] IWebhookIntegrationRepository integrations,
        [FromServices] IFabStorageReadiness storage,
        [FromServices] IClock clock,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(request).IsNotNull();

        var logger = loggerFactory.CreateLogger(typeof(EventsEndpoints));

        var integration = await WebhookDeliveryAuthentication.AuthenticateWebhookAsync(
            integrationName, request, fabId, integrations, logger, cache, cancellationToken);
        if (integration is null)
        {
            return Results.Unauthorized();
        }

        // FR-009: the machine path gets the same refusal. Asked after
        // authentication, so the fab is the integration's own (spec 018's
        // amendment) rather than one the caller merely named — and asked before
        // the channel, for the reason above.
        if (!await storage.IsReadyAsync(integration.Fab, cancellationToken))
        {
            return FabNotProvisioned(integration.Fab);
        }

        EventEnvelope envelope;
        try
        {
            envelope = WebhookEnvelope(integration, body, fabId, clock);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return (await EventIngestStore.StoreOrRefuseAsync(limiter, scopeFactory, envelope, cancellationToken)).Match(
            onSuccess: identifier => Results.Created($"/events/{identifier}", identifier),
            onFailure: problem => problem);
    }

    private static EventEnvelope WebhookEnvelope(
        WebhookIntegration integration,
        IngestWebhookEventRequest body,
        string fabId,
        IClock clock) =>
        new(
            EventIdentifier.New(),
            FabIdentifier.From(fabId),
            Source.Webhook,
            DeviceIdentifier.From(integration.Name.Value),
            Kind.From(body.Kind ?? integration.DefaultKind.Value),
            OccurredAt.From(body.OccurredAt ?? clock.UtcNow),
            Payload.From(body.Payload));

    /// <summary>
    /// 503, not 400 and not 403 (spec 019). The request is well-formed and the
    /// caller is entitled to that fab — nothing about what they did is wrong.
    /// The system is not ready to store it, and the condition is temporary by
    /// construction: the next provisioning run fixes it. No <c>Retry-After</c>,
    /// because how long is genuinely unknown and a made-up number is worse than
    /// none.
    /// </summary>
    private static IResult FabNotProvisioned(FabIdentifier fab) =>
        Results.Problem(
            title: "EVENT_FAB_NOT_PROVISIONED",
            detail: $"Fab '{fab.Value}' has no event storage yet, so this event cannot be stored. "
                + "It has not been lost — it was never accepted. Retry once provisioning has run.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
