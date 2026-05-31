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
        ArgumentNullException.ThrowIfNull(command);

        Option<WebhookIntegration> found = await integrations
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>.Failure(
                new RevokeWebhookIntegrationError.WebhookIntegrationNotFound(command.Name.Value));
        }

        WebhookIntegration integration = found.Value;
        integration.Revoke(clock);
        await integrations.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Revoked webhook integration '{Name}' ({Identifier}).", integration.Name, integration.Id);

        return Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>.Success(integration.Id);
    }
}
