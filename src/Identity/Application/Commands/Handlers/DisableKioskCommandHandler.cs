using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

public sealed class DisableKioskCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<DisableKioskCommandHandler> logger)
    : ICommandHandler<DisableKioskCommand, Result<RegisteredClientIdentifier, DisableKioskError>>
{
    public async Task<Result<RegisteredClientIdentifier, DisableKioskError>> HandleAsync(
        DisableKioskCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (ClientId clientId, FabIdentifier fab) = command;

        Option<RegisteredClientAggregate> found = await clients
            .GetWithinFabAsync(clientId, fab, cancellationToken);
        if (!found.HasValue || found.Value.Kind != ClientKind.Kiosk)
        {
            return Failure(DisableKioskFailures.KioskNotFound(clientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(clientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(DisableKioskFailures.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledKiosk(client.Id, clientId);

        return Success(client.Id);
    }
}
