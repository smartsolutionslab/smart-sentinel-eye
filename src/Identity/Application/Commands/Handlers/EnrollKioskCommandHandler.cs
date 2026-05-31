using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

public sealed class EnrollKioskCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<EnrollKioskCommandHandler> logger)
    : ICommandHandler<EnrollKioskCommand, Result<KioskCredentialsDto, EnrollKioskError>>
{
    public async Task<Result<KioskCredentialsDto, EnrollKioskError>> HandleAsync(
        EnrollKioskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (clientId, fab, enrolledBy) = command;

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<KioskCredentialsDto, EnrollKioskError>.Failure(
                new EnrollKioskError.KioskAlreadyEnrolled(clientId.Value));
        }

        KeycloakClientRepresentation representation = new(
            ClientId: clientId.Value,
            Name: $"Kiosk {clientId.Value}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: KeycloakScopeBundles.Kiosk,
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "kiosk",
                ["sse.fab"] = fab.Value,
            });

        KeycloakClientCredentials credentials;
        try
        {
            credentials = await keycloak.CreateClientAsync(
                representation,
                fabGroupPath: $"/fabs/{fab.Value}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (KeycloakClientAlreadyExistsException ex)
        {
            return Result<KioskCredentialsDto, EnrollKioskError>.Failure(
                new EnrollKioskError.KioskAlreadyEnrolled(ex.ClientId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<KioskCredentialsDto, EnrollKioskError>.Failure(
                new EnrollKioskError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate registered = RegisteredClientAggregate.Register(
            clientId, ClientKind.Kiosk, fab, enrolledBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.EnrolledKiosk(logger, registered.Id, clientId, fab);

        return Result<KioskCredentialsDto, EnrollKioskError>.Success(
            new KioskCredentialsDto(
                registered.Id.Value,
                clientId.Value,
                fab.Value,
                credentials.ClientSecret));
    }
}
