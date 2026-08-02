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

public class DisableKioskCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static void Seed(InMemoryRegisteredClientRepository repo, ClientKind kind, string clientId)
    {
        RegisteredClientAggregate aggregate = new RegisteredClientBuilder()
            .WithClientId(clientId)
            .WithKind(kind)
            .WithClock(Now)
            .Build();
        repo.Add(aggregate);
    }

    [Fact]
    public async Task Happy_path_disables_the_kiosk()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.Kiosk, "kiosk-3");
        FakeKeycloakAdminClient keycloak = new();

        DisableKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableKioskCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(ClientId.From("kiosk-3")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        keycloak.Disabled.ShouldContain("kiosk-3");
        repo.Clients[0].DisabledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_clientId_returns_KioskNotFound()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        DisableKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableKioskCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(ClientId.From("ghost")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableKioskError.KioskNotFound>();
    }

    [Fact]
    public async Task Device_clientId_returns_KioskNotFound()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.Device, "plc-station-4");
        FakeKeycloakAdminClient keycloak = new();

        DisableKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableKioskCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(ClientId.From("plc-station-4")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableKioskError.KioskNotFound>();
    }
}
