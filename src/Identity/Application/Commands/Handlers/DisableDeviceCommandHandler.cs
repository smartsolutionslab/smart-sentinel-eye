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
            return Result<RegisteredClientIdentifier, DisableDeviceError>.Failure(
                new DisableDeviceError.DeviceNotFound(command.ClientId.Value));
        }

        // ADR-0113 Layer 1: refuse an edit built on a view of the device that
        // has since moved. Checked ahead of the Keycloak call, not just ahead
        // of the local write — disabling the Keycloak client is a real,
        // unwound-by-nothing side effect, and a stale request must not cause it.
        if (found.Value.Version != command.ExpectedVersion)
        {
            return Result<RegisteredClientIdentifier, DisableDeviceError>.Failure(
                new DisableDeviceError.DeviceStale(
                    command.ClientId.Value, command.ExpectedVersion, found.Value.Version));
        }

        try
        {
            await keycloak.DisableClientAsync(command.ClientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<RegisteredClientIdentifier, DisableDeviceError>.Failure(
                new DisableDeviceError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledDevice(client.Id, command.ClientId);

        return Result<RegisteredClientIdentifier, DisableDeviceError>.Success(client.Id);
    }
}
