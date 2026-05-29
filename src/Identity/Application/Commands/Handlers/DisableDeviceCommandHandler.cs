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
    ILogger<DisableDeviceCommandHandler> log)
    : ICommandHandler<DisableDeviceCommand, Result<RegisteredClientIdentifier, DisableDeviceError>>
{
    public async Task<Result<RegisteredClientIdentifier, DisableDeviceError>> HandleAsync(
        DisableDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<RegisteredClientAggregate> found = await clients
            .GetByClientIdAsync(command.ClientId, cancellationToken).ConfigureAwait(false);
        if (!found.HasValue || found.Value.Kind != ClientKind.Device)
        {
            return Result<RegisteredClientIdentifier, DisableDeviceError>.Failure(
                new DisableDeviceError.DeviceNotFound(command.ClientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(command.ClientId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<RegisteredClientIdentifier, DisableDeviceError>.Failure(
                new DisableDeviceError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Disabled device {Identifier} '{ClientId}'.", client.Id, command.ClientId);

        return Result<RegisteredClientIdentifier, DisableDeviceError>.Success(client.Id);
    }
}
