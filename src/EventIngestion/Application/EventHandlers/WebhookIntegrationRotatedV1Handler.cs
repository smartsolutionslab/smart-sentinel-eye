using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that flips a webhook integration's
/// bearer-validation path from legacy hash-compare to Keycloak-JWT
/// validate (spec 008 FR-016). Idempotent: a replay or at-least-once
/// re-delivery against an already-rotated integration carrying the
/// same clientId is a no-op.
/// </summary>
public sealed class WebhookIntegrationRotatedV1Handler(IWebhookIntegrationRepository integrations, IClock clock, ILogger<WebhookIntegrationRotatedV1Handler> logger)
{
    public async Task Handle(WebhookIntegrationRotatedV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (integrationName, clientId, _, metadata) = message;

        WebhookIntegrationName name;
        try
        {
            name = WebhookIntegrationName.From(integrationName);
        }
        catch (ArgumentException ex)
        {
            logger.InvalidRotationName(ex, integrationName);
            return;
        }

        FabIdentifier fab;
        try
        {
            // A null or unparsable fab is refused rather than resolved by
            // name alone: this handler mutates a security-relevant
            // validation mode, and an unscoped resolve is exactly how AS-5
            // (spec 182) lets a first rotation in one fab take over an
            // unrotated integration registered in another.
            fab = FabIdentifier.From(metadata.Fab ?? string.Empty);
        }
        catch (ArgumentException)
        {
            logger.RotationFabMismatch(integrationName, metadata.Fab ?? "(none)");
            return;
        }

        Option<WebhookIntegration> found = await integrations.GetWithinFabAsync(fab, name, cancellationToken);
        if (!found.HasValue)
        {
            logger.RotationTargetMissing(integrationName);
            return;
        }

        WebhookIntegration integration = found.Value;
        integration.MarkAsRotated(KeycloakClientIdentifier.From(clientId), clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationFlippedToJwt(integrationName, clientId);
    }
}
