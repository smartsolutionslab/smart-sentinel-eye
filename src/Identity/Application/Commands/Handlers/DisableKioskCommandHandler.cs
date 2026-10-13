using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

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
        (var clientId, var fab) = command;

        var found = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
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

        var client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledKiosk(client.Id, clientId);

        return Success(client.Id);
    }
}
