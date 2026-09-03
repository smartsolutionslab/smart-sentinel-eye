using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Queries;

/// <summary>
/// ADR-0142's replay path. It rebuilds the original answer rather than reading a
/// stored copy of it, which is what keeps a plaintext secret out of our database
/// — so the test that matters is that the secret comes back <b>unchanged</b>.
/// </summary>
public class ReplayDeviceRegistrationQueryHandlerTests
{
    private const string DeviceType = "plc";
    private const string DeviceIdentifier = "t040-01";
    private const string Fab = "munich";

    [Fact]
    public async Task A_replay_returns_the_same_secret_rather_than_a_rotated_one()
    {
        (ReplayDeviceRegistrationQueryHandler handler, RegisteredClientAggregate registered,
            FakeKeycloakAdminClient keycloak) = Arrange();
        string original = keycloak.CurrentSecrets[registered.ClientId.Value];

        Result<DeviceCredentialsDto, ReplayRegistrationError> result =
            await handler.HandleAsync(Query(registered.Id), CancellationToken.None);

        DeviceCredentialsDto dto = result.Match(dto => dto, _ => throw new InvalidOperationException("expected success"));
        dto.ClientSecret.ShouldBe(
            original,
            "rotating on a replay would hand the retry a different secret and silently invalidate the one "
            + "the first attempt already delivered.");
        keycloak.CurrentSecrets[registered.ClientId.Value].ShouldBe(original, "the read must not change it.");
    }

    [Fact]
    public async Task A_replay_carries_the_device_details_from_the_repeated_request()
    {
        (ReplayDeviceRegistrationQueryHandler handler, RegisteredClientAggregate registered, _) = Arrange();

        Result<DeviceCredentialsDto, ReplayRegistrationError> result =
            await handler.HandleAsync(Query(registered.Id), CancellationToken.None);

        DeviceCredentialsDto dto = result.Match(dto => dto, _ => throw new InvalidOperationException("expected success"));
        dto.RegisteredClientIdentifier.ShouldBe(registered.Id.Value);
        dto.ClientId.ShouldBe(registered.ClientId.Value);
        dto.DeviceType.ShouldBe(DeviceType);
        dto.DeviceIdentifier.ShouldBe(DeviceIdentifier);
        dto.Fab.ShouldBe(Fab);
    }

    /// <summary>
    /// A key naming a registration that no longer exists must refuse rather than
    /// fall through to registering again — that would turn a retry into a second,
    /// silent creation.
    /// </summary>
    [Fact]
    public async Task A_key_naming_a_registration_that_is_gone_is_refused()
    {
        (ReplayDeviceRegistrationQueryHandler handler, _, _) = Arrange();

        Result<DeviceCredentialsDto, ReplayRegistrationError> result = await handler.HandleAsync(
            Query(RegisteredClientIdentifier.From(Guid.Parse("0198f1c0-0000-7000-8000-0000000000ff"))),
            CancellationToken.None);

        result.Match(_ => "success", error => error.Code).ShouldBe("REPLAYED_REGISTRATION_MISSING");
    }

    /// <summary>
    /// The registration survives here but Keycloak has lost the client, so the
    /// answer cannot be rebuilt. Refused rather than re-created, for the same
    /// reason as above.
    /// </summary>
    [Fact]
    public async Task A_client_Keycloak_no_longer_holds_is_refused_rather_than_recreated()
    {
        (ReplayDeviceRegistrationQueryHandler handler, RegisteredClientAggregate registered,
            FakeKeycloakAdminClient keycloak) = Arrange(registerInKeycloak: false);

        Result<DeviceCredentialsDto, ReplayRegistrationError> result =
            await handler.HandleAsync(Query(registered.Id), CancellationToken.None);

        result.Match(_ => "success", error => error.Code).ShouldBe("REPLAYED_CLIENT_MISSING");
        keycloak.CallCount.ShouldBe(1, "it asked Keycloak once and took no for an answer.");
    }

    private static ReplayDeviceRegistrationQuery Query(RegisteredClientIdentifier client) =>
        new(client, DeviceType, DeviceIdentifier);

    private static (ReplayDeviceRegistrationQueryHandler Handler,
        RegisteredClientAggregate Registered,
        FakeKeycloakAdminClient Keycloak) Arrange(bool registerInKeycloak = true)
    {
        ClientId clientId = ClientId.From($"{DeviceType}-{DeviceIdentifier}");
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();

        RegisteredClientAggregate registered = RegisteredClientAggregate.Register(
            clientId,
            ClientKind.Device,
            FabIdentifier.From(Fab),
            OperatorIdentifier.From(Guid.Parse("0198f1c0-0000-7000-8000-00000000000a")),
            new FakeClock(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero)));

        clients.Add(registered);

        if (registerInKeycloak)
        {
            keycloak.CreateClientAsync(
                new KeycloakClientRepresentation(
                    clientId.Value,
                    clientId.Value,
                    ServiceAccountsEnabled: true,
                    StandardFlowEnabled: false,
                    DirectAccessGrantsEnabled: false,
                    PublicClient: false,
                    DefaultClientScopes: [],
                    OptionalClientScopes: [],
                    Attributes: new Dictionary<string, string>(StringComparer.Ordinal)),
                $"/fabs/{Fab}",
                CancellationToken.None).GetAwaiter().GetResult();
            keycloak.CallCount.ShouldBe(1);
        }

        return (new ReplayDeviceRegistrationQueryHandler(clients, keycloak), registered, keycloak);
    }
}
