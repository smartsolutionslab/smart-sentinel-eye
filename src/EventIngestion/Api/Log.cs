using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Api;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    // Spec 302 (#2205). The external response to a revoked-integration
    // delivery stays the same collapsed 401 as a never-registered name
    // (spec 102) — this is the only place the distinction is recorded.
    // Carries the integration's own name, identifier, fab and revocation
    // time; never the bearer token, the request body, or the
    // caller-supplied fabId (which need not even match the integration's
    // own, per IsIntegrationsOwnFab).
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refused a delivery to revoked webhook integration '{Name}' ({Identifier}) in fab {Fab}; revoked at {RevokedAt}.")]
    public static partial void RevokedWebhookIntegrationDeliveryRefused(
        this ILogger logger, WebhookIntegrationName name, WebhookIntegrationIdentifier identifier,
        FabIdentifier fab, RevokedAt revokedAt);
}
