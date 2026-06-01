using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
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

        WebhookIntegrationName name;
        try
        {
            name = WebhookIntegrationName.From(message.IntegrationName);
        }
        catch (ArgumentException ex)
        {
            logger.InvalidRotationName(ex, message.IntegrationName);
            return;
        }

        Option<WebhookIntegration> found = await integrations.GetByNameAsync(name, cancellationToken);
        if (!found.HasValue)
        {
            logger.RotationTargetMissing(message.IntegrationName);
            return;
        }

        WebhookIntegration integration = found.Value;
        integration.MarkAsRotated(message.ClientId, clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationFlippedToJwt(message.IntegrationName, message.ClientId);
    }
}
