using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class RegisterDeviceCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static RegisterDeviceCommand HappyCommand(
        string deviceType = "plc", string deviceIdentifier = "station-4") =>
        new(
            deviceType,
            deviceIdentifier,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task Happy_path_creates_a_Keycloak_client_with_devicetype_deviceid_clientId()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("plc-station-4");
        result.Value.DeviceType.ShouldBe("plc");
        result.Value.DeviceIdentifier.ShouldBe("station-4");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.Device);
    }

    [Fact]
    public async Task Re_registration_returns_DeviceAlreadyRegistered()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(), CancellationToken.None);
        Result<DeviceCredentialsDto, RegisterDeviceError> second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<RegisterDeviceError.DeviceAlreadyRegistered>();
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("webhook")]
    [InlineData("not-a-source")]
    public async Task Invalid_device_type_returns_InvalidDeviceType(string badType)
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(deviceType: badType), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceType>();
        repo.Clients.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_device_identifier_returns_InvalidDeviceIdentifier()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        // A deviceIdentifier containing whitespace yields a clientId
        // 'plc-has space' that fails the ClientId grammar.
        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(
                HappyCommand(deviceIdentifier: "has space"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.InvalidDeviceIdentifier>();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak timeout" };
        RegisterDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        Result<DeviceCredentialsDto, RegisterDeviceError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterDeviceError.KeycloakUnavailable>();
    }
}
