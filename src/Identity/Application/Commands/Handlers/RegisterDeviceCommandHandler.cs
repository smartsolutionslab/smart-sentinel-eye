using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

public sealed class RegisterDeviceCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<RegisterDeviceCommandHandler> log)
    : ICommandHandler<RegisterDeviceCommand, Result<DeviceCredentialsDto, RegisterDeviceError>>
{
    private static readonly string[] AllowedDeviceTypes = ["plc", "inference"];

    public async Task<Result<DeviceCredentialsDto, RegisterDeviceError>> HandleAsync(
        RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AllowedDeviceTypes.Contains(command.DeviceType, StringComparer.Ordinal))
        {
            return Result<DeviceCredentialsDto, RegisterDeviceError>.Failure(
                new RegisterDeviceError.InvalidDeviceType(command.DeviceType));
        }

        ClientId clientId;
        try
        {
            clientId = ClientId.From($"{command.DeviceType}-{command.DeviceIdentifier}");
        }
        catch (ArgumentException ex)
        {
            return Result<DeviceCredentialsDto, RegisterDeviceError>.Failure(
                new RegisterDeviceError.InvalidDeviceIdentifier(ex.Message));
        }

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<DeviceCredentialsDto, RegisterDeviceError>.Failure(
                new RegisterDeviceError.DeviceAlreadyRegistered(clientId.Value));
        }

        KeycloakClientRepresentation representation = new(
            ClientId: clientId.Value,
            Name: $"{command.DeviceType} {command.DeviceIdentifier}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: KeycloakScopeBundles.Device,
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "device",
                ["sse.deviceType"] = command.DeviceType,
                ["sse.deviceIdentifier"] = command.DeviceIdentifier,
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
            return Result<DeviceCredentialsDto, RegisterDeviceError>.Failure(
                new RegisterDeviceError.DeviceAlreadyRegistered(ex.ClientId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<DeviceCredentialsDto, RegisterDeviceError>.Failure(
                new RegisterDeviceError.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate registered = RegisteredClientAggregate.Register(
            clientId, ClientKind.Device, command.Fab, command.RegisteredBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Registered device {Identifier} '{ClientId}' ({DeviceType}/{DeviceIdentifier}) for fab {Fab}.",
            registered.Id, clientId, command.DeviceType, command.DeviceIdentifier, command.Fab);

        return Result<DeviceCredentialsDto, RegisterDeviceError>.Success(
            new DeviceCredentialsDto(
                registered.Id.Value,
                clientId.Value,
                command.DeviceType,
                command.DeviceIdentifier,
                command.Fab.Value,
                credentials.ClientSecret));
    }
}
