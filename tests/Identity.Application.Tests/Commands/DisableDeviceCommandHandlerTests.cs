using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;
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
        InMemoryRegisteredClientRepository repo, ClientKind kind, string clientId, string fab = "munich")
    {
        RegisteredClientAggregate aggregate = new RegisteredClientBuilder()
            .WithClientId(clientId)
            .WithKind(kind)
            .WithFab(fab)
            .WithClock(Now)
            .Build();
        repo.Seed(aggregate);
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
            new DisableDeviceCommand(ClientId.From("plc-station-4"), FabIdentifier.From("munich")), CancellationToken.None);

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
            new DisableDeviceCommand(ClientId.From("ghost"), FabIdentifier.From("munich")), CancellationToken.None);

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
            new DisableDeviceCommand(ClientId.From("kiosk-3"), FabIdentifier.From("munich")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableDeviceError.DeviceNotFound>();
    }

    /// <summary>
    /// The device exists; the caller may not know that. The refusal must be
    /// the same one an unknown clientId produces, because a distinguishable
    /// answer lets an operator enumerate another fab's devices.
    /// </summary>
    [Fact]
    public async Task Another_fabs_device_returns_DeviceNotFound_with_the_same_message_as_unknown()
    {
        InMemoryRegisteredClientRepository repo = new();
        SeedAggregate(repo, ClientKind.Device, "plc-station-4", "dresden");
        FakeKeycloakAdminClient keycloak = new();

        DisableDeviceCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableDeviceCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await handler.HandleAsync(
            new DisableDeviceCommand(ClientId.From("plc-station-4"), FabIdentifier.From("munich")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableDeviceError.DeviceNotFound>();

        // The message must not differ either — the fab is what it would leak.
        result.Error.Message.ShouldBe("No registered device with clientId 'plc-station-4' exists.");

        // And nothing happened to it.
        repo.Clients[0].DisabledAt.ShouldBeNull();
    }
}
