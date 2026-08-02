using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1 for Identity. Every rejection test also asserts Keycloak
/// was never called — these handlers mutate an external system before they
/// touch the aggregate, so a check placed after that call would return the
/// right error having already disabled a live client or rolled a live secret.
/// Neither is undone by returning a failure.
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Disabling_a_device_on_a_stale_version_touches_neither_Keycloak_nor_the_aggregate()
    {
        (InMemoryRegisteredClientRepository clients, FakeKeycloakAdminClient keycloak) =
            Seeded(ClientKind.Device, "plc-station-4");

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await new DisableDeviceCommandHandler(
                clients, keycloak, new FakeClock(Now.AddHours(1)),
                NullLogger<DisableDeviceCommandHandler>.Instance)
            .HandleAsync(new DisableDeviceCommand(ClientId.From("plc-station-4"), Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DEVICE_STALE");
        keycloak.Disabled.ShouldBeEmpty();
        clients.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
    }

    [Fact]
    public async Task Disabling_a_kiosk_on_a_stale_version_touches_neither_Keycloak_nor_the_aggregate()
    {
        (InMemoryRegisteredClientRepository clients, FakeKeycloakAdminClient keycloak) =
            Seeded(ClientKind.Kiosk, "kiosk-3");

        Result<RegisteredClientIdentifier, DisableKioskError> result = await new DisableKioskCommandHandler(
                clients, keycloak, new FakeClock(Now.AddHours(1)),
                NullLogger<DisableKioskCommandHandler>.Instance)
            .HandleAsync(new DisableKioskCommand(ClientId.From("kiosk-3"), Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("KIOSK_STALE");
        keycloak.Disabled.ShouldBeEmpty();
        clients.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
    }

    [Fact]
    public async Task Re_rotating_a_webhook_client_on_a_stale_version_leaves_the_live_secret_alone()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = Rotator(clients, keycloak, bus);

        // The first rotation is what registers the client; only after it
        // exists is there a version to be stale against.
        (await handler.HandleAsync(Rotation(0), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        string minted = keycloak.CurrentSecrets["webhook-qa"];

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(Rotation(Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WEBHOOK_CLIENT_STALE");
        keycloak.CurrentSecrets["webhook-qa"].ShouldBe(minted);
        bus.Published.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_first_time_rotation_is_not_gated_because_there_is_no_prior_version()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        RotateWebhookClientCommandHandler handler = Rotator(clients, keycloak, new FakeEventBus());

        // A caller migrating a grandfathered spec-006 integration has no
        // RegisteredClient to have read a version from. The register branch
        // must ignore whatever they sent rather than reject them.
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(Rotation(Stale), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        clients.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.WebhookIntegration);
    }

    [Fact]
    public async Task Each_rotation_hands_back_the_version_the_next_one_must_send()
    {
        // Webhook clients appear in no list endpoint — both filter to devices
        // and kiosks — so the rotation response is the caller's only source
        // for the version. If it stopped carrying one, every rotation from the
        // third on would 409 with nothing the caller could do about it.
        RotateWebhookClientCommandHandler handler = Rotator(
            new InMemoryRegisteredClientRepository(), new FakeKeycloakAdminClient(), new FakeEventBus());

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> first =
            await handler.HandleAsync(Rotation(0), CancellationToken.None);
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> second =
            await handler.HandleAsync(Rotation(first.Value.Version), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryRegisteredClientRepository clients, FakeKeycloakAdminClient keycloak) =
            Seeded(ClientKind.Device, "plc-station-4");
        RegisteredClientAggregate device = clients.Clients[0];

        Result<RegisteredClientIdentifier, DisableDeviceError> result = await new DisableDeviceCommandHandler(
                clients, keycloak, new FakeClock(Now.AddHours(1)),
                NullLogger<DisableDeviceCommandHandler>.Instance)
            .HandleAsync(
                new DisableDeviceCommand(ClientId.From("plc-station-4"), device.Version),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        keycloak.Disabled.ShouldContain("plc-station-4");
    }

    private static RotateWebhookClientCommand Rotation(int expectedVersion) =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), expectedVersion);

    private static RotateWebhookClientCommandHandler Rotator(
        InMemoryRegisteredClientRepository clients, FakeKeycloakAdminClient keycloak, FakeEventBus bus) =>
        new(clients, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

    private static (InMemoryRegisteredClientRepository, FakeKeycloakAdminClient) Seeded(
        ClientKind kind, string clientId)
    {
        InMemoryRegisteredClientRepository clients = new();
        clients.Add(new RegisteredClientBuilder()
            .WithClientId(clientId)
            .WithKind(kind)
            .WithClock(Now)
            .Build());

        return (clients, new FakeKeycloakAdminClient());
    }
}
