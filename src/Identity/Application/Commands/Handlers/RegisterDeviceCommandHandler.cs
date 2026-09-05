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
    ILogger<RegisterDeviceCommandHandler> logger)
    : ICommandHandler<RegisterDeviceCommand, Result<DeviceCredentialsDto, RegisterDeviceError>>
{
    private static readonly string[] AllowedDeviceTypes = ["plc", "inference"];

    public async Task<Result<DeviceCredentialsDto, RegisterDeviceError>> HandleAsync(
        RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (string? deviceType, string? deviceIdentifier, FabIdentifier? fab, OperatorIdentifier registeredBy) = command;

        if (!AllowedDeviceTypes.Contains(deviceType, StringComparer.Ordinal))
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceType(deviceType));
        }

        ClientId clientId;
        try
        {
            clientId = ClientId.From($"{deviceType}-{deviceIdentifier}");
        }
        catch (ArgumentException ex)
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier(ex.Message));
        }

        Option<RegisteredClientAggregate> existing = await clients
            .GetByClientIdAsync(clientId, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(RegisterDeviceFailures.DeviceAlreadyRegistered(clientId.Value));
        }

        KeycloakClientRepresentation representation = new(
            ClientId: clientId.Value,
            Name: $"{deviceType} {deviceIdentifier}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: [.. KeycloakScopeBundles.Device, KeycloakScopeBundles.AudienceScope],
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "device",
                ["sse.deviceType"] = deviceType,
                ["sse.deviceIdentifier"] = deviceIdentifier,
                ["sse.fab"] = fab.Value,
            });

        KeycloakClientCredentials credentials;
        try
        {
            credentials = await keycloak.CreateClientAsync(
                representation,
                fabGroupPath: $"/fabs/{fab.Value}",
                cancellationToken);
        }
        catch (KeycloakClientAlreadyExistsException ex)
        {
            return Failure(RegisterDeviceFailures.DeviceAlreadyRegistered(ex.ClientId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(RegisterDeviceFailures.KeycloakUnavailable(ex.Message));
        }

        RegisteredClientAggregate registered = RegisteredClientAggregate.Register(
            clientId, ClientKind.Device, fab, registeredBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken);

        logger.RegisteredDevice(registered.Id, clientId, deviceType, deviceIdentifier, fab);

        return Success(
            new DeviceCredentialsDto(
                registered.Id.Value,
                clientId.Value,
                deviceType,
                deviceIdentifier,
                fab.Value,
                credentials.ClientSecret));
    }
}
