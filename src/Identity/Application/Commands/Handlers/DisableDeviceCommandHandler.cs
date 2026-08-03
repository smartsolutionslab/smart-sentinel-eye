using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

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

        Option<RegisteredClientAggregate> found = await clients
            .GetByClientIdAsync(command.ClientId, cancellationToken);
        if (!found.HasValue || found.Value.Kind != ClientKind.Device)
        {
            return Failure(DisableDeviceFailures.DeviceNotFound(command.ClientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(command.ClientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(DisableDeviceFailures.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledDevice(client.Id, command.ClientId);

        return Success(client.Id);
    }
}
