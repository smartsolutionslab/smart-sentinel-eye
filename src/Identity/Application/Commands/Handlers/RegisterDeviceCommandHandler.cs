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
        (var deviceType, var deviceIdentifier, var fab, var registeredBy) = command;

        var clientIdResult = ToClientId(deviceType, deviceIdentifier, fab);
        if (clientIdResult.IsFailure)
        {
            return Failure(clientIdResult.Error);
        }

        var clientId = clientIdResult.Value;

        var existing = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(RegisterDeviceFailures.DeviceAlreadyRegistered(clientId.Value));
        }

        var representation = DeviceRepresentation(clientId, deviceType, deviceIdentifier, fab);
        var created = await CreateInKeycloakAsync(representation, fab, cancellationToken);
        if (created.IsFailure)
        {
            return Failure(created.Error);
        }

        var registered = await RegisterAsync(clientId, fab, registeredBy, cancellationToken);

        logger.RegisteredDevice(registered.Id, clientId, deviceType, deviceIdentifier, fab);

        return Success(
            registered.ToDeviceCredentialsDto(
                deviceType,
                deviceIdentifier,
                created.Value.ClientSecret));
    }

    private static Result<ClientId, RegisterDeviceError> ToClientId(
        string deviceType,
        string deviceIdentifier,
        FabIdentifier fab)
    {
        if (!AllowedDeviceTypes.Contains(deviceType, StringComparer.Ordinal))
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceType(deviceType));
        }

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier("must not be empty."));
        }

        if (!IsHyphenSeparatedAlphanumeric(deviceIdentifier))
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier(
                "each hyphen-separated segment must be one or more letters or digits."));
        }

        try
        {
            return Success(ClientId.From($"{deviceType}.{fab.Value}.{deviceIdentifier}"));
        }
        catch (ArgumentException ex)
        {
            return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier(ex.Message));
        }
    }

    private static KeycloakClientRepresentation DeviceRepresentation(
        ClientId clientId,
        string deviceType,
        string deviceIdentifier,
        FabIdentifier fab) =>
        new(
            ClientId: clientId.Value,
            Name: $"{deviceType} {deviceIdentifier}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes:
                [.. KeycloakScopeBundles.Device, KeycloakScopeBundles.AudienceScope, KeycloakScopeBundles.GroupsScope],
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "device",
                ["sse.deviceType"] = deviceType,
                ["sse.deviceIdentifier"] = deviceIdentifier,
                ["sse.fab"] = fab.Value,
            });

    private async Task<Result<KeycloakClientCredentials, RegisterDeviceError>> CreateInKeycloakAsync(
        KeycloakClientRepresentation representation,
        FabIdentifier fab,
        CancellationToken cancellationToken)
    {
        try
        {
            return Success(await keycloak.CreateClientAsync(
                representation,
                fabGroupPath: $"/fabs/{fab.Value}",
                cancellationToken));
        }
        catch (KeycloakClientAlreadyExistsException ex)
        {
            return Failure(RegisterDeviceFailures.DeviceAlreadyRegistered(ex.ClientId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(RegisterDeviceFailures.KeycloakUnavailable(ex.Message));
        }
    }

    private async Task<RegisteredClientAggregate> RegisterAsync(
        ClientId clientId,
        FabIdentifier fab,
        OperatorIdentifier registeredBy,
        CancellationToken cancellationToken)
    {
        var registered = RegisteredClientAggregate.Register(
            clientId, ClientKind.Device, fab, registeredBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken);

        return registered;
    }

    private static bool IsHyphenSeparatedAlphanumeric(string deviceIdentifier) =>
        deviceIdentifier.Split('-').All(segment => segment.Length > 0 && segment.All(char.IsLetterOrDigit));
}
