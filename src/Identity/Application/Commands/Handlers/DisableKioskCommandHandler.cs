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

        Option<RegisteredClientAggregate> found = await clients
            .GetByClientIdAsync(command.ClientId, cancellationToken);
        if (!found.HasValue || found.Value.Kind != ClientKind.Kiosk)
        {
            return Result<RegisteredClientIdentifier, DisableKioskError>.Failure(
                new DisableKioskError.KioskNotFound(command.ClientId.Value));
        }

        // ADR-0113 Layer 1: refuse an edit built on a view of the kiosk that
        // has since moved. Checked ahead of the Keycloak call, not just ahead
        // of the local write — disabling the Keycloak client is a real,
        // unwound-by-nothing side effect, and a stale request must not cause it.
        if (found.Value.Version != command.ExpectedVersion)
        {
            return Result<RegisteredClientIdentifier, DisableKioskError>.Failure(
                new DisableKioskError.KioskStale(
                    command.ClientId.Value, command.ExpectedVersion, found.Value.Version));
        }

        try
        {
            await keycloak.DisableClientAsync(command.ClientId.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<RegisteredClientIdentifier, DisableKioskError>.Failure(
                new DisableKioskError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate client = found.Value;
        client.Disable(clock);
        await clients.SaveAsync(cancellationToken);

        logger.DisabledKiosk(client.Id, command.ClientId);

        return Result<RegisteredClientIdentifier, DisableKioskError>.Success(client.Id);
    }
}
