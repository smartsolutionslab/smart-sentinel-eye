using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Write-path handlers for <see cref="EventsEndpoints"/>.</summary>
public static partial class EventsEndpoints
{
    private const string BearerPrefix = "Bearer ";

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

        IngestWriteLimiter limiter = services.Limiter;
        IServiceScopeFactory scopeFactory = services.ScopeFactory;
        IFabAuthorizationGuard fabGuard = services.FabGuard;
        IFabStorageReadiness storage = services.Storage;

        if (!IdempotencyHeaders.TryRead(http.Request, out Option<IdempotencyKey> key, out IResult? keyProblem))
        {
            return keyProblem;
        }

        // Resolved from the caller, never from the request (spec 018 FR-006).
        // Before this, `fabId` went straight into the envelope unchecked, so an
        // operator could file an event against any plant — where it drives that
        // plant's automation rules and changes what its operators see.
        //
        // Resolved BEFORE anything is written (spec 018 FR-007): a refusal that
        // had already stored would place a fabricated event in another plant's
        // stream while reporting that it had been stopped.
        Result<FabIdentifier, IResult> fabResolution =
            await EventIngestionFabResolution.ResolveWriteFabAsync(user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;

        // Spec 019 FR-007. Still checked first: it names the cause precisely
        // ("this plant has no storage") where the write below could only report
        // that something failed. Since spec 020 the write is synchronous, so
        // neither answer can be given after the event was already accepted.
        if (!await storage.IsReadyAsync(fab, cancellationToken))
        {
            return FabNotProvisioned(fab);
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

        // The envelope already carries a fresh EventIdentifier, so a retry without
        // a key would file a second event under a second identifier — no conflict
        // to notice, just a duplicate. With a key the caller gets the first
        // event's identifier back instead.
        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied, IngestManualEndpoint, user.ToOperatorIdentifier().Value.ToString())),
                services.Idempotency,
                services.Clock),
            identifier => $"/events/{identifier}",
            token => StoreOrRefuseAsync(limiter, scopeFactory, envelope, token),
            cancellationToken);
    }

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
        [FromServices] TimeProvider Clock);

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

        return (await StoreOrRefuseAsync(limiter, scopeFactory, envelope, cancellationToken)).Match(
            onSuccess: identifier => Results.Created($"/events/{identifier}", identifier),
            onFailure: problem => problem);
    }

    /// <summary>
    /// Authenticates a webhook caller and returns the matching integration,
    /// or <c>null</c> if the bearer token is missing/malformed, the
    /// integration is unknown or revoked, the token fails validation, or the
    /// delivery names a plant other than the integration's own.
    /// Every failure path collapses to <c>null</c> so the 401 response never
    /// leaks which integrations exist — including, now, whether one exists in
    /// another plant.
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

        // The delivery's plant must be the integration's own, in BOTH modes and
        // before the token is even considered (#1545). Until this, only the JWT
        // branch looked at the fab — StaticHash, the default and the mode of
        // every integration until it is rotated, matched the hash and let
        // `?fabId=` through unchecked, so a token issued for one plant could
        // file events into another. It is the same manipulation FR-006 closed on
        // the manual write, and the reason it survived is that there was no fab
        // on the integration to compare against.
        if (!IsIntegrationsOwnFab(integration, fabId))
        {
            return null;
        }

        bool authorized = integration.ValidationMode == BearerValidationMode.Jwt
            ? await ValidateJwtAsync(request, integration, fabId)
            : integration.TokenHash.Matches(token);
        return authorized ? integration : null;
    }

    /// <summary>
    /// Whether the delivery names the integration's own plant. An unparseable
    /// <c>fabId</c> is not its plant either, so it collapses to the same
    /// refusal rather than a 400 that would confirm the integration exists.
    /// </summary>
    private static bool IsIntegrationsOwnFab(WebhookIntegration integration, string fabId)
    {
        try
        {
            return integration.Fab == FabIdentifier.From(fabId);
        }
        catch (ArgumentException)
        {
            return false;
        }
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
        if (!string.Equals(azp, integration.KeycloakClientId?.Value, StringComparison.Ordinal))
        {
            return false;
        }

        string targetGroup = "/fabs/" + fabId;
        return user.FindAll("groups").Any(claim =>
            claim.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Contains(targetGroup, StringComparer.Ordinal));
    }

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

    /// <summary>
    /// Stores the event, then answers (spec 020 FR-001). Replaces the enqueue
    /// that answered 202 the moment the envelope was buffered.
    ///
    /// <para>
    /// <b>201, not 202.</b> 202 means "accepted for processing, outcome
    /// unknown", which is exactly the promise this feature removes — and the
    /// promise that used to be broken silently whenever the write failed
    /// afterwards. Once the row is committed before the response, 201 with a
    /// <c>Location</c> is the truthful answer, and the location resolves.
    /// </para>
    ///
    /// <para>
    /// <b>429 when the limiter is saturated</b>, not when a channel is full.
    /// These writes no longer use the channel, so without this the endpoint
    /// would quietly become "queue indefinitely and time out" — a worse answer
    /// to overload than a fast refusal, arrived at by omission (FR-013).
    /// </para>
    /// </summary>
    /// <summary>
    /// Yields the stored event's identifier, or the problem to answer with.
    ///
    /// <para>
    /// Returns the identifier rather than a finished <c>IResult</c> so the manual
    /// path can hand it to <c>IdempotentRequest.ExecuteCreateAsync</c>, which
    /// needs to record what was created (ADR-0142). The webhook path maps it
    /// straight back to a 201 and is otherwise unchanged.
    /// </para>
    /// </summary>
    private static async Task<Result<Guid, IResult>> StoreOrRefuseAsync(
        IngestWriteLimiter limiter,
        IServiceScopeFactory scopeFactory,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using IngestWriteLease lease = limiter.TryAcquire();
        if (!lease.Acquired)
        {
            return Result<Guid, IResult>.Failure(Results.Problem(
                title: "EVENT_INGEST_BACKPRESSURE",
                detail: "Too many events are being stored at once; please retry.",
                statusCode: StatusCodes.Status429TooManyRequests));
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IngestEventCommandHandler handler =
            scope.ServiceProvider.GetRequiredService<IngestEventCommandHandler>();

        try
        {
            Result<EventIdentifier, IngestEventError> result =
                await handler.HandleAsync(new IngestEventCommand(envelope), cancellationToken);

            return result.Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The caller is told, which is the whole point: a 202 followed by
            // silence is what this replaces. Nothing has been stored and
            // nothing has been buffered, so retrying is safe and is the
            // caller's to decide.
            return Result<Guid, IResult>.Failure(Results.Problem(
                title: "EVENT_NOT_STORED",
                detail: "The event could not be stored and has not been accepted. Retry.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }
}
