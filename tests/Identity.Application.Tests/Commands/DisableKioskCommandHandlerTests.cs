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

    private static void Seed(
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
    public async Task Happy_path_disables_the_kiosk()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.Kiosk, "kiosk-3");
        FakeKeycloakAdminClient keycloak = new();

        DisableKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableKioskCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(ClientId.From("kiosk-3"), FabIdentifier.From("munich")), CancellationToken.None);

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
            new DisableKioskCommand(ClientId.From("ghost"), FabIdentifier.From("munich")), CancellationToken.None);

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
            new DisableKioskCommand(ClientId.From("plc-station-4"), FabIdentifier.From("munich")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableKioskError.KioskNotFound>();
    }

    /// <summary>
    /// The kiosk exists; the caller may not know that. The refusal must be
    /// the same one an unknown clientId produces, because a distinguishable
    /// answer lets an operator enumerate another fab's kiosks.
    /// </summary>
    [Fact]
    public async Task Another_fabs_kiosk_returns_KioskNotFound_with_the_same_message_as_unknown()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.Kiosk, "kiosk-3", "dresden");
        FakeKeycloakAdminClient keycloak = new();

        DisableKioskCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableKioskCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableKioskError> result = await handler.HandleAsync(
            new DisableKioskCommand(ClientId.From("kiosk-3"), FabIdentifier.From("munich")), CancellationToken.None);

        result.Error.ShouldBeOfType<DisableKioskError.KioskNotFound>();

        // The message must not differ either — the fab is what it would leak.
        result.Error.Message.ShouldBe("No enrolled kiosk with clientId 'kiosk-3' exists.");

        // And nothing happened to it.
        repo.Clients[0].DisabledAt.ShouldBeNull();
    }
}
