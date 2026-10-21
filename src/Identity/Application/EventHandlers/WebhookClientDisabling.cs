using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

internal static class WebhookClientDisabling
{
    public static async Task DisableOrThrowForRetryAsync(
        ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> handler,
        ILogger logger,
        string integrationName,
        FabIdentifier fab,
        CancellationToken cancellationToken)
    {
        ClientId clientId;
        try
        {
            clientId = ClientId.From($"webhook-{integrationName}");
        }
        catch (ArgumentException ex)
        {
            logger.InvalidRevocationName(ex, integrationName);
            return;
        }

        var result = await handler.HandleAsync(
            new DisableWebhookClientCommand(clientId, fab), cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error is DisableWebhookClientError.WebhookClientNotFound)
            {
                logger.NoWebhookClientToDisable(integrationName, fab);
                return;
            }

            logger.WebhookClientDisableFailed(integrationName, result.Error.Code, result.Error.Message);

            // Wolverine treats an exception as a retry signal. A Keycloak outage
            // must not be swallowed — swallowing it would leave the client
            // enabled with nothing left to retry.
            throw new WebhookClientDisableFailedException(integrationName, result.Error.Code);
        }
    }
}
