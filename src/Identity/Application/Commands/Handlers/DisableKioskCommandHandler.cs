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
        ArgumentNullException.ThrowIfNull(command);

        Option<RegisteredClientAggregate> found = await clients
            .GetByClientIdAsync(command.ClientId, cancellationToken).ConfigureAwait(false);
        if (!found.HasValue || found.Value.Kind != ClientKind.Kiosk)
        {
            return Result<RegisteredClientIdentifier, DisableKioskError>.Failure(
                new DisableKioskError.KioskNotFound(command.ClientId.Value));
        }

        try
        {
            await keycloak.DisableClientAsync(command.ClientId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<RegisteredClientIdentifier, DisableKioskError>.Failure(
                new DisableKioskError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Disabled kiosk {Identifier} '{ClientId}'.", client.Id, command.ClientId);

        return Result<RegisteredClientIdentifier, DisableKioskError>.Success(client.Id);
    }
}
