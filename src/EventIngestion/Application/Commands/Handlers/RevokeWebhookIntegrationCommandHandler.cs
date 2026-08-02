using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

public sealed class RevokeWebhookIntegrationCommandHandler(
    IWebhookIntegrationRepository integrations,
    IClock clock,
    ILogger<RevokeWebhookIntegrationCommandHandler> logger)
    : ICommandHandler<
        RevokeWebhookIntegrationCommand,
        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>>
{
    public async Task<Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>> HandleAsync(
        RevokeWebhookIntegrationCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        Option<WebhookIntegration> found = await integrations
            .GetByNameAsync(command.Name, cancellationToken);
        if (!found.HasValue)
        {
            return Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>.Failure(
                new RevokeWebhookIntegrationError.WebhookIntegrationNotFound(command.Name.Value));
        }

        WebhookIntegration integration = found.Value;

        // ADR-0113 Layer 1: refuse a revoke built on a view of the integration
        // that has since moved. Checked before any mutation so nothing is
        // applied on top of stale intent.
        if (integration.Version != command.ExpectedVersion)
        {
            return Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>.Failure(
                new RevokeWebhookIntegrationError.WebhookIntegrationStale(
                    command.Name.Value, command.ExpectedVersion, integration.Version));
        }

        integration.Revoke(clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationRevoked(integration.Name, integration.Id);

        return Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>.Success(integration.Id);
    }
}
