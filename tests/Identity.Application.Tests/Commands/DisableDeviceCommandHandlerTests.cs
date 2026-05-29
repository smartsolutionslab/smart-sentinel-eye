using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class DisableDeviceCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static void SeedDevice(InMemoryRegisteredClientRepository repo) =>
        SeedAggregate(repo, ClientKind.Device, "plc-station-4");

    private static void SeedAggregate(
        InMemoryRegisteredClientRepository repo, ClientKind kind, string clientId)
    {
        RegisteredClientAggregate aggregate = RegisteredClientAggregate.Register(
            ClientId.From(clientId),
            kind,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Add(aggregate);
    }

    [Fact]
    public async Task Happy_path_disables_the_Keycloak_client_and_the_local_aggregate()
    {
        InMemoryRegisteredClientRepository repo = new();
        SeedDevice(repo);
        FakeKeycloakAdminClient keycloak = new();

        DisableDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableDeviceCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await handler.HandleAsync(
            new DisableDeviceCommand(ClientId.From("plc-station-4")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        keycloak.Disabled.ShouldContain("plc-station-4");
        repo.Clients.ShouldHaveSingleItem().DisabledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_clientId_returns_DeviceNotFound()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();

        DisableDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableDeviceCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await handler.HandleAsync(
            new DisableDeviceCommand(ClientId.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<DisableDeviceError.DeviceNotFound>();
    }

    [Fact]
    public async Task ClientId_owned_by_a_kiosk_returns_DeviceNotFound()
    {
        // Disabling a kiosk via the device endpoint is a category
        // error; both 404s look identical from outside.
        InMemoryRegisteredClientRepository repo = new();
        SeedAggregate(repo, ClientKind.Kiosk, "kiosk-3");
        FakeKeycloakAdminClient keycloak = new();

        DisableDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableDeviceCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await handler.HandleAsync(
            new DisableDeviceCommand(ClientId.From("kiosk-3")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableDeviceError.DeviceNotFound>();
    }
}
