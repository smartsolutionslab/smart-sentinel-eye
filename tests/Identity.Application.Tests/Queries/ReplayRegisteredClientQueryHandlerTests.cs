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
/// ADR-0142's replay path, shared by all three credential endpoints. It rebuilds
/// the answer rather than reading a stored copy, which is what keeps a plaintext
/// secret out of our database — so the test that matters most is that the secret
/// comes back <b>unchanged</b>.
/// </summary>
public class ReplayRegisteredClientQueryHandlerTests
{
    private const string ClientIdValue = "plc-t040-01";
    private const string Fab = "munich";

    [Fact]
    public async Task A_replay_returns_the_same_secret_rather_than_a_rotated_one()
    {
        (ReplayRegisteredClientQueryHandler handler, RegisteredClientAggregate registered,
            FakeKeycloakAdminClient keycloak) = Arrange();
        string original = keycloak.CurrentSecrets[ClientIdValue];

        ReplayedClientDto replayed = Succeeds(
            await handler.HandleAsync(new ReplayRegisteredClientQuery(registered.Id), CancellationToken.None));

        replayed.ClientSecret.ShouldBe(
            original,
            "rotating on a replay would hand the retry a different secret and silently invalidate the one "
            + "the first attempt already delivered.");
        keycloak.CurrentSecrets[ClientIdValue].ShouldBe(original, "reading must not change it.");
    }

    [Fact]
    public async Task A_replay_carries_the_state_the_server_holds()
    {
        (ReplayRegisteredClientQueryHandler handler, RegisteredClientAggregate registered, _) = Arrange();

        ReplayedClientDto replayed = Succeeds(
            await handler.HandleAsync(new ReplayRegisteredClientQuery(registered.Id), CancellationToken.None));

        replayed.RegisteredClientIdentifier.ShouldBe(registered.Id.Value);
        replayed.ClientId.ShouldBe(ClientIdValue);
        replayed.Fab.ShouldBe(Fab);
        replayed.Version.ShouldBe(registered.Version);
    }

    /// <summary>
    /// A key naming a registration that no longer exists must refuse rather than
    /// fall through to minting again — that would turn a retry into a second,
    /// silent creation, which is the outcome the mechanism exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_key_naming_a_registration_that_is_gone_is_refused()
    {
        (ReplayRegisteredClientQueryHandler handler, _, _) = Arrange();

        Result<ReplayedClientDto, ReplayRegistrationError> result = await handler.HandleAsync(
            new ReplayRegisteredClientQuery(
                RegisteredClientIdentifier.From(Guid.Parse("0198f1c0-0000-7000-8000-0000000000ff"))),
            CancellationToken.None);

        Code(result).ShouldBe("REPLAYED_REGISTRATION_MISSING");
    }

    [Fact]
    public async Task A_client_Keycloak_no_longer_holds_is_refused_rather_than_recreated()
    {
        (ReplayRegisteredClientQueryHandler handler, RegisteredClientAggregate registered,
            FakeKeycloakAdminClient keycloak) = Arrange(registerInKeycloak: false);

        Result<ReplayedClientDto, ReplayRegistrationError> result =
            await handler.HandleAsync(new ReplayRegisteredClientQuery(registered.Id), CancellationToken.None);

        Code(result).ShouldBe("REPLAYED_CLIENT_MISSING");
        keycloak.CallCount.ShouldBe(1, "it asked Keycloak once and took no for an answer.");
    }

    private static ReplayedClientDto Succeeds(Result<ReplayedClientDto, ReplayRegistrationError> result) =>
        result.Match(dto => dto, error => throw new InvalidOperationException($"expected success, got {error.Code}"));

    private static string Code(Result<ReplayedClientDto, ReplayRegistrationError> result) =>
        result.Match(_ => "success", error => error.Code);

    private static (ReplayRegisteredClientQueryHandler Handler,
        RegisteredClientAggregate Registered,
        FakeKeycloakAdminClient Keycloak) Arrange(bool registerInKeycloak = true)
    {
        ClientId clientId = ClientId.From(ClientIdValue);
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
        }

        return (new ReplayRegisteredClientQueryHandler(clients, keycloak), registered, keycloak);
    }
}
