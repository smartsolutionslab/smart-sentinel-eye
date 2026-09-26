using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

/// <summary>
/// Mirrors <see cref="DisableKioskCommandHandler"/> / <see cref="DisableDeviceCommandHandler"/>:
/// Keycloak first, then the row, so a Keycloak failure never leaves the row
/// disabled without the client actually being disabled (spec 264, #2206).
/// </summary>
public sealed class DisableWebhookClientCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<DisableWebhookClientCommandHandler> logger)
    : ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>
{
    public async Task<Result<RegisteredClientIdentifier, DisableWebhookClientError>> HandleAsync(
        DisableWebhookClientCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (ClientId clientId, FabIdentifier fab) = command;

        Option<RegisteredClientAggregate> found = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
        if (!found.HasValue || found.Value.Kind != ClientKind.WebhookIntegration)
        {
            return Failure(DisableWebhookClientFailures.WebhookClientNotFound(clientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(clientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(DisableWebhookClientFailures.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledWebhookClient(client.Id, clientId);

        return Success(client.Id);
    }
}
