using System.Net;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using SmartSentinelEye.Shared.Kernel.Tests;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Queries;

/// <summary>
/// Spec 454 (#3096). A replay that names the version its original answer carried is refused
/// once the client has moved on, and a refusal reads no secret, or discards the one it read.
/// The unbound replay devices and kiosks keep is pinned by
/// <see cref="ReplayRegisteredClientQueryHandlerTests"/>, which must stay untouched.
/// </summary>
public class ReplayRegisteredClientQueryHandlerSupersededTests
{
    private const string ClientIdValue = "webhook-t454-01";
    private const string Fab = "munich";

    [Fact]
    public async Task A_bound_replay_while_the_client_is_still_at_the_answered_version_returns_the_current_secret()
    {
        var harness = Arrange(rowVersion: 3, versionAtRecheck: Option<AggregateVersion>.Some(AggregateVersion.From(3)));
        var secret = harness.Keycloak.CurrentSecrets[ClientIdValue];

        var result = await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None);

        var replayed = Succeeds(result);
        replayed.ClientSecret.ShouldBe(secret);
        replayed.Version.ShouldBe(3);
    }

    [Fact]
    public async Task A_bound_replay_after_the_row_moved_on_is_refused_as_superseded_without_reading_the_secret()
    {
        var harness = Arrange(rowVersion: 4, versionAtRecheck: Option<AggregateVersion>.Some(AggregateVersion.From(4)));

        var result = await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None);

        var error = Fails(result);
        error.Code.ShouldBe("IDEMPOTENCY_KEY_SUPERSEDED");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
        harness.KeycloakCalls.ShouldBe(0, "a superseded key must not read a secret at all.");
    }

    [Fact]
    public async Task A_refusal_names_no_secret_version_fab_or_client()
    {
        var harness = Arrange(rowVersion: 4, versionAtRecheck: Option<AggregateVersion>.Some(AggregateVersion.From(4)));
        var secret = harness.Keycloak.CurrentSecrets[ClientIdValue];

        var error = Fails(await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None));

        error.ShouldBeOfType<ReplayedKeySuperseded>();
        error.Message.ShouldNotContain(secret);
        error.Message.ShouldNotContain(Fab);
        error.Message.ShouldNotContain(ClientIdValue);
        error.Message.ShouldNotContain("3");
        error.Message.ShouldNotContain("4");
    }

    [Fact]
    public async Task A_bound_replay_whose_client_is_rotated_after_the_first_check_is_refused_and_the_secret_discarded()
    {
        var harness = Arrange(rowVersion: 3, versionAtRecheck: Option<AggregateVersion>.Some(AggregateVersion.From(4)));
        var secret = harness.Keycloak.CurrentSecrets[ClientIdValue];

        var result = await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None);

        var error = Fails(result);
        error.Code.ShouldBe("IDEMPOTENCY_KEY_SUPERSEDED");
        error.Message.ShouldNotContain(secret);
        harness.KeycloakCalls.ShouldBe(1, "the secret was read once, then dropped when the re-check disagreed.");
    }

    [Fact]
    public async Task The_version_is_re_read_after_the_secret_not_before_it()
    {
        var harness = Arrange(rowVersion: 3, versionAtRecheck: Option<AggregateVersion>.Some(AggregateVersion.From(3)));
        await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None);

        harness.Clients.KeycloakCallsWhenVersionWasReread.ShouldBe(1, "a rotation that lands during the Keycloak read is only visible to a re-check made after it.");
    }

    [Fact]
    public async Task A_bound_replay_whose_client_vanishes_at_the_re_check_is_refused_as_superseded()
    {
        var harness = Arrange(rowVersion: 3, versionAtRecheck: Option<AggregateVersion>.None);

        var result = await harness.Handler.HandleAsync(harness.QueryAnsweredAt(3), CancellationToken.None);

        Fails(result).Code.ShouldBe("IDEMPOTENCY_KEY_SUPERSEDED");
    }

    [Fact]
    public async Task An_unbound_replay_never_asks_for_the_version()
    {
        var harness = Arrange(rowVersion: 7, versionAtRecheck: Option<AggregateVersion>.None);

        var result = await harness.Handler.HandleAsync(
            new ReplayRegisteredClientQuery(harness.Registered.Id, Option<AggregateVersion>.None),
            CancellationToken.None);

        Succeeds(result).Version.ShouldBe(7);
        harness.Clients.VersionReads.ShouldBe(0);
    }

    private static ReplayedClientDto Succeeds(Result<ReplayedClientDto, ReplayRegistrationError> result) =>
        result.Match(dto => dto, error => throw new InvalidOperationException($"expected success, got {error.Code}"));

    private static ReplayRegistrationError Fails(Result<ReplayedClientDto, ReplayRegistrationError> result) =>
        result.Match<ReplayRegistrationError>(
            _ => throw new InvalidOperationException("expected a refusal, got a success"),
            error => error);

    private static Harness Arrange(int rowVersion, Option<AggregateVersion> versionAtRecheck)
    {
        var clientId = ClientId.From(ClientIdValue);
        FakeKeycloakAdminClient keycloak = new();

        var registered = RegisteredClientAggregate.Register(
            clientId,
            ClientKind.WebhookIntegration,
            FabIdentifier.From(Fab),
            OperatorIdentifier.From(Guid.Parse("0198f1c0-0000-7000-8000-00000000000a")),
            new FakeClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)));
        AggregateVersions.SetTo(registered, rowVersion);

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
        var baseline = keycloak.CallCount;

        RecheckRepository clients = new(registered, versionAtRecheck, () => keycloak.CallCount - baseline);

        return new Harness(
            new ReplayRegisteredClientQueryHandler(clients, keycloak),
            registered,
            keycloak,
            clients,
            baseline);
    }

    /// <summary>
    /// Serves the one registration for the first read and a configurable answer for the version
    /// re-check, so a test can move the version between the two, and records how many Keycloak
    /// calls had happened when the re-check ran.
    /// </summary>
    private sealed class RecheckRepository(
        RegisteredClientAggregate registered,
        Option<AggregateVersion> versionAtRecheck,
        Func<int> keycloakCalls) : IRegisteredClientRepository
    {
        public int VersionReads { get; private set; }

        public int KeycloakCallsWhenVersionWasReread { get; private set; } = -1;

        public Task<Option<RegisteredClientAggregate>> GetByIdentifierAsync(
            RegisteredClientIdentifier identifier, CancellationToken cancellationToken) =>
            Task.FromResult(Option<RegisteredClientAggregate>.Some(registered));

        public Task<Option<AggregateVersion>> GetVersionAsync(
            RegisteredClientIdentifier identifier, CancellationToken cancellationToken)
        {
            VersionReads++;
            KeycloakCallsWhenVersionWasReread = keycloakCalls();

            return Task.FromResult(versionAtRecheck);
        }

        public Task<Option<RegisteredClientAggregate>> GetByClientIdAsync(
            ClientId clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Option<RegisteredClientAggregate>> GetWithinFabAsync(
            FabIdentifier fab, ClientId clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(RegisteredClientAggregate client) => throw new NotSupportedException();

        public Task SaveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlySet<ClientId>> GetActiveClientIdsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record Harness(
        ReplayRegisteredClientQueryHandler Handler,
        RegisteredClientAggregate Registered,
        FakeKeycloakAdminClient Keycloak,
        RecheckRepository Clients,
        int Baseline)
    {
        public int KeycloakCalls => Keycloak.CallCount - Baseline;

        public ReplayRegisteredClientQuery QueryAnsweredAt(int version) =>
            new(Registered.Id, Option<AggregateVersion>.Some(AggregateVersion.From(version)));
    }
}
