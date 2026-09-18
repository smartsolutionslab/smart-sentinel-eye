using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class RotateWebhookClientCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    // None is the "create it, it does not exist yet" intent (If-None-Match: *).
    // These tests all start from an empty repository, so that is the branch
    // they mean; Some(v) would be refused as WEBHOOK_CLIENT_NOT_FOUND.
    private static RotateWebhookClientCommand HappyCommand(string name = "qa") =>
        new(name, FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.None);

    // Spec 182 US1 / AS-4. A munich-only operator naming their own fab, sending
    // If-Match: "7" — the version Dresden's own client is really at. A bare fab
    // guard at the endpoint passes this honestly; only a fab-scoped lookup can
    // tell the caller does not hold the fab the *client* is actually in.
    private static RotateWebhookClientCommand CrossFabAttackCommand() =>
        new(
            "dresden-line-3", FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.Some(7));

    // What AS-4 and AS-9 must answer identically — same clientId, same
    // expected version, derived from CrossFabAttackCommand — so a future
    // change to either branch's error construction that the other does not
    // follow fails both tests below rather than neither.
    private static RotateWebhookClientError ExpectedCrossFabRefusal() =>
        RotateWebhookClientFailures.WebhookClientNotFound("webhook-dresden-line-3", 7);

    [Fact]
    public async Task First_rotation_creates_the_Keycloak_client_and_publishes_WebhookIntegrationRotatedV1()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("webhook-qa");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.WebhookIntegration);

        WebhookIntegrationRotatedV1 published = bus.Published
            .OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        published.IntegrationName.ShouldBe("qa");
        published.ClientId.ShouldBe("webhook-qa");
    }

    [Fact]
    public async Task Second_rotation_rolls_the_secret_without_creating_a_second_aggregate()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> first =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        // The second call is an update, so it carries the version the first
        // handed back rather than repeating the create intent.
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> second =
            await handler.HandleAsync(
                HappyCommand() with { ExpectedVersion = Option<int>.Some(first.Value.Version) },
                CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        second.Value.ClientSecret.ShouldBe("secret-webhook-qa-rotated");
        repo.Clients.Count.ShouldBe(1);
        repo.Clients[0].LastRotatedAt.ShouldNotBeNull();
        bus.Published.OfType<WebhookIntegrationRotatedV1>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_integration_name_returns_InvalidIntegrationName()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        // "Has Spaces" yields clientId "webhook-Has Spaces" which
        // fails the ClientId grammar.
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler
            .HandleAsync(HappyCommand("Has Spaces"), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.InvalidIntegrationName>();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak 500" };
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.KeycloakUnavailable>();
        repo.Clients.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 182 US1 (#2280), AS-4 — the defect. Dresden's webhook client
    /// already exists at version 7. A munich-only operator names <b>their
    /// own</b> fab, not Dresden's, and sends If-Match: "7" — the version the
    /// victim's client is genuinely at. Today's unscoped
    /// <c>GetByClientIdAsync</c> finds it anyway, the version check passes
    /// honestly, and the secret is rolled and handed back to the attacker.
    /// </summary>
    [Fact]
    public async Task A_munich_operator_naming_its_own_fab_cannot_rotate_a_dresden_registered_webhook_client()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient dresdenClient = RegisteredClient.Register(
            ClientId.From("webhook-dresden-line-3"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("dresden"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(dresdenClient, version: 7);

        // Dresden's Keycloak client is planted directly, the same way
        // RotateWebhookClientCommandHandler's own create branch would have
        // minted it — so the rotate branch below finds a real client to
        // roll, and a red run shows the actual vulnerability (a 200 with a
        // live secret) rather than an unrelated KEYCLOAK_UNAVAILABLE from a
        // fake that never heard of the clientId.
        FakeKeycloakAdminClient keycloak = new();
        await keycloak.CreateClientAsync(
            new KeycloakClientRepresentation(
                ClientId: "webhook-dresden-line-3",
                Name: "Webhook dresden-line-3",
                ServiceAccountsEnabled: true,
                StandardFlowEnabled: false,
                DirectAccessGrantsEnabled: false,
                PublicClient: false,
                DefaultClientScopes: [],
                OptionalClientScopes: [],
                Attributes: new Dictionary<string, string>()),
            fabGroupPath: "/fabs/dresden",
            CancellationToken.None);
        string dresdenSecretBeforeAttack = keycloak.CurrentSecrets["webhook-dresden-line-3"];
        int keycloakCallsBeforeAttack = keycloak.CallCount;

        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(CrossFabAttackCommand(), CancellationToken.None);

        string evidence = result.IsSuccess
            ? $"succeeded with clientId '{result.Value.ClientId}' and clientSecret '{result.Value.ClientSecret}'"
            : $"correctly refused with {result.Error}";
        result.IsSuccess.ShouldBeFalse(
            "a munich-only operator naming its own fab must not be able to rotate Dresden's webhook "
            + $"client — that is credential disclosure plus destruction; {evidence}");
        result.Error.ShouldBe(
            ExpectedCrossFabRefusal(),
            $"got {result.Error}; the refusal must be indistinguishable from AS-9's unknown-name case, "
            + "or the route becomes an oracle for what another fab runs");
        keycloak.CallCount.ShouldBe(
            keycloakCallsBeforeAttack,
            "Dresden's Keycloak client must never be reached by a rotation naming another fab");
        keycloak.CurrentSecrets["webhook-dresden-line-3"].ShouldBe(
            dresdenSecretBeforeAttack,
            "Dresden's live secret must be unchanged by a refused cross-fab rotation");
        bus.Published.ShouldBeEmpty(
            "no WebhookIntegrationRotatedV1 may be published for a rotation that never happened");
    }

    /// <summary>
    /// Spec 182 US1 (#2280), AS-9 — the property that makes AS-4's refusal
    /// safe rather than merely correct. The identical command as
    /// <see cref="A_munich_operator_naming_its_own_fab_cannot_rotate_a_dresden_registered_webhook_client"/>,
    /// this time against a repository where the name has never been
    /// registered in any fab. A caller must not be able to tell "exists
    /// elsewhere" from "exists nowhere" — both compare against
    /// <see cref="ExpectedCrossFabRefusal"/>, so a future change to either
    /// branch's error construction that the other does not follow fails both
    /// tests rather than neither. Green both before and after the fix: an
    /// empty repository returns <c>None</c> whether the lookup is scoped to a
    /// fab or not.
    /// </summary>
    [Fact]
    public async Task A_client_registered_in_another_fab_and_a_name_that_exists_nowhere_get_the_identical_refusal()
    {
        InMemoryRegisteredClientRepository repo = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, new FakeKeycloakAdminClient(), new FakeEventBus(), new NoOpTransactionalCommit(),
            new FakeClock(Now), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(CrossFabAttackCommand(), CancellationToken.None);

        result.Error.ShouldBe(
            ExpectedCrossFabRefusal(),
            $"got {result.Error}; a name that exists nowhere must answer exactly as a name that exists "
            + "in another fab does");
    }
}
