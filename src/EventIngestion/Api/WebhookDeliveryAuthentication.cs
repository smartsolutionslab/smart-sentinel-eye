using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Bearer authentication for the webhook route of <see cref="EventsEndpoints.IngestWebhook"/>.</summary>
internal static class WebhookDeliveryAuthentication
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Authenticates a webhook caller and returns the matching integration,
    /// or <c>null</c> if the bearer token is missing/malformed, the
    /// integration is unknown or revoked, the token fails validation, or the
    /// delivery names a plant other than the integration's own.
    /// Every failure path collapses to <c>null</c> so the 401 response never
    /// leaks which integrations exist — including, now, whether one exists in
    /// another plant. A revoked integration is additionally written to an
    /// internal application log (spec 302, #2205), at most once per
    /// <see cref="RevokedWebhookDeliveryLogWindow"/> per integration; every
    /// other refusal stays silent, exactly as before.
    /// </summary>
    internal static async Task<WebhookIntegration?> AuthenticateWebhookAsync(
        string integrationName,
        HttpRequest request,
        string fabId,
        IWebhookIntegrationRepository integrations,
        ILogger logger,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        string? authHeader = request.Headers.Authorization;
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var token = authHeader[BearerPrefix.Length..];

        var found = await FindIntegrationAsync(integrationName, integrations, cancellationToken);
        if (!found.HasValue)
        {
            return null;
        }

        var integration = found.Value;

        if (IsRevokedLoggingOnce(integration, logger, cache))
        {
            return null;
        }

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

        var authorized = integration.ValidationMode == BearerValidationMode.Jwt
            ? await ValidateJwtAsync(request, integration, fabId)
            : integration.TokenHash.Matches(token);
        return authorized ? integration : null;
    }

    private static async Task<Option<WebhookIntegration>> FindIntegrationAsync(
        string integrationName,
        IWebhookIntegrationRepository integrations,
        CancellationToken cancellationToken)
    {
        WebhookIntegrationName parsedName;
        try
        {
            parsedName = WebhookIntegrationName.From(integrationName);
        }
        catch (ArgumentException)
        {
            return Option<WebhookIntegration>.None;
        }

        return await integrations.GetByNameAsync(parsedName, cancellationToken);
    }

    private static bool IsRevokedLoggingOnce(WebhookIntegration integration, ILogger logger, IMemoryCache cache)
    {
        if (integration.RevokedAt is not RevokedAt revokedAt)
        {
            return false;
        }

        if (IsNewRevokedWebhookDeliveryLog(cache, integration.Id))
        {
            logger.RevokedWebhookIntegrationDeliveryRefused(
                integration.Name, integration.Id, integration.Fab, revokedAt);
        }

        return true;
    }

    /// <summary>
    /// Dedup window for the revoked-webhook-delivery log (review finding on
    /// #2205): matches <c>WhepAuthorizeRateLimiting</c>'s production default
    /// (<c>StreamDistribution/Api/appsettings.json</c>'s <c>Window: "00:01:00"</c>).
    /// This route is <c>AllowAnonymous</c> and the revoked check runs before any
    /// credential or fab check, so an integration's <b>name</b> — public, it is
    /// in every sender's config and in the URL path, never secret — is enough
    /// for anyone to trigger this branch at an unlimited rate; logging on every
    /// refusal would let that caller flood the one OTLP sink ADR-0118 gives
    /// this service.
    /// </summary>
    private static readonly TimeSpan RevokedWebhookDeliveryLogWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Mirrors the <c>WhepAuthorizeActionPartition</c> (StreamDistribution)
    /// <c>IsNewWhepAuthorizeThrottleTransition</c>: an <see cref="IMemoryCache"/>
    /// entry per key, absolute-expiring after <see cref="RevokedWebhookDeliveryLogWindow"/>,
    /// gives "log once per transition, re-arm once a full window has passed"
    /// without a hand-rolled dictionary that never evicts. Keyed on the
    /// integration's identifier, not the caller, so the window is shared by
    /// every anonymous caller refused for the same revoked integration.
    /// </summary>
    private static bool IsNewRevokedWebhookDeliveryLog(
        IMemoryCache cache, WebhookIntegrationIdentifier identifier)
    {
        var key = $"revoked-webhook-delivery-log:{identifier.Value}";
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, RevokedWebhookDeliveryLogWindow);
        return true;
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
        var auth = await request.HttpContext
            .AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null)
        {
            return false;
        }

        var user = auth.Principal;

        var hasEventsWriteScope = user.FindAll("scope").Any(claim =>
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("sse.events.write", StringComparer.Ordinal));
        if (!hasEventsWriteScope)
        {
            return false;
        }

        var azp = user.FindFirst("azp")?.Value;
        if (!string.Equals(azp, integration.KeycloakClientId?.Value, StringComparison.Ordinal))
        {
            return false;
        }

        var targetGroup = "/fabs/" + fabId;
        return user.FindAll("groups").Any(claim =>
            claim.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Contains(targetGroup, StringComparer.Ordinal));
    }
}
