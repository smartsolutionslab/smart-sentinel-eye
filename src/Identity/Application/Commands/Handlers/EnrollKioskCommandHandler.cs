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
    ILogger<EnrollKioskCommandHandler> log)
    : ICommandHandler<EnrollKioskCommand, Result<KioskCredentialsDto, EnrollKioskError>>
{
    public async Task<Result<KioskCredentialsDto, EnrollKioskError>> HandleAsync(
        EnrollKioskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(command.ClientId, cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<KioskCredentialsDto, EnrollKioskError>.Failure(
                new EnrollKioskError.KioskAlreadyEnrolled(command.ClientId.Value));
        }

        KeycloakClientRepresentation representation = new(
            ClientId: command.ClientId.Value,
            Name: $"Kiosk {command.ClientId.Value}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: KeycloakScopeBundles.Kiosk,
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "kiosk",
                ["sse.fab"] = command.Fab.Value,
            });

        KeycloakClientCredentials credentials;
        try
        {
            credentials = await keycloak.CreateClientAsync(
                representation,
                fabGroupPath: $"/fabs/{command.Fab.Value}",
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
            command.ClientId, ClientKind.Kiosk, command.Fab, command.EnrolledBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Enrolled kiosk {Identifier} '{ClientId}' for fab {Fab}.",
            registered.Id, command.ClientId, command.Fab);

        return Result<KioskCredentialsDto, EnrollKioskError>.Success(
            new KioskCredentialsDto(
                registered.Id.Value,
                command.ClientId.Value,
                command.Fab.Value,
                credentials.ClientSecret));
    }
}
