using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

public sealed class DisableDeviceCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<DisableDeviceCommandHandler> logger)
    : ICommandHandler<DisableDeviceCommand, Result<RegisteredClientIdentifier, DisableDeviceError>>
{
    public async Task<Result<RegisteredClientIdentifier, DisableDeviceError>> HandleAsync(
        DisableDeviceCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (var clientId, var fab) = command;

        var found = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
        if (!found.HasValue || found.Value.Kind != ClientKind.Device)
        {
            return Failure(DisableDeviceFailures.DeviceNotFound(clientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(clientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(DisableDeviceFailures.KeycloakUnavailable(ex.Message));
        }

        var client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledDevice(client.Id, clientId);

        return Success(client.Id);
    }
}
