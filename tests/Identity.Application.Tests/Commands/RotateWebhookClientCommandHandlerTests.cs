using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
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

    // Spec 318 (#2628) S4: RotateWebhookClientCommandHandler now calls
    // DisableWebhookClientCommand's own handler for its race-path disable
    // instead of duplicating its steps inline. None of the tests below this
    // helper exercises that race path (status stays Active throughout, or the
    // handler never reaches the create branch), so a throwaway, independent
    // instance is correct here — it is never touched. The handful of tests
    // that DO exercise the race path wire the real repo/keycloak instead, so
    // the disable actually lands on the aggregate under test.
    private static DisableWebhookClientCommandHandler NoOpDisableWebhookClient() =>
        new(
            new InMemoryRegisteredClientRepository(), new FakeKeycloakAdminClient(),
            new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance);

    private static DisableWebhookClientCommandHandler DisableWebhookClientUsing(
        InMemoryRegisteredClientRepository repo, FakeKeycloakAdminClient keycloak) =>
        new(repo, keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance);

    [Fact]
    public async Task First_rotation_creates_the_Keycloak_client_and_publishes_WebhookIntegrationRotatedV1()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

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
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

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
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

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
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.KeycloakUnavailable>();
        repo.Clients.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 254 AS-4 (#2570) — the handler contract the fix pins. The rotate
    /// branch's <c>SaveAsync</c> is where the EF Layer-2 concurrency token
    /// lives (ADR-0113); a genuine loser throws a raw
    /// <see cref="DbUpdateConcurrencyException"/> there, before Keycloak is
    /// ever asked to roll the secret. It must reach
    /// <c>ConcurrencyConflictExceptionHandler</c> unconverted, not be folded
    /// into <c>KeycloakUnavailable</c> by the handler's own catch.
    ///
    /// <para>
    /// Pre-fix red: the current filter does not exclude
    /// <see cref="DbUpdateConcurrencyException"/>, so the handler catches it
    /// and <i>returns</i> a <c>KeycloakUnavailable</c> result instead of
    /// throwing — <see cref="ShouldThrowExtensions.ShouldThrowAsync"/> finds
    /// nothing to catch.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_rotation_that_loses_the_database_race_lets_the_concurrency_exception_reach_the_middleware()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(client, version: 3);
        repo.FailNextSaveWith = new DbUpdateConcurrencyException(
            "Layer-2 loser: the row was updated by another rotation first.");

        FakeKeycloakAdminClient keycloak = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), new FakeEventBus(),
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        RotateWebhookClientCommand command = new(
            "qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()),
            Option<int>.Some(3));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            async () => await handler.HandleAsync(command, CancellationToken.None));

        keycloak.CallCount.ShouldBe(
            0, "the loser must not roll a secret for a save that never committed");
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
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

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
            repo, new FakeKeycloakAdminClient(), new FakeWebhookIntegrationStatusLookup(),
            new FakeEventBus(), new NoOpTransactionalCommit(),
            new FakeClock(Now), NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(CrossFabAttackCommand(), CancellationToken.None);

        result.Error.ShouldBe(
            ExpectedCrossFabRefusal(),
            $"got {result.Error}; a name that exists nowhere must answer exactly as a name that exists "
            + "in another fab does");
    }

    /// <summary>
    /// Issue #2749. Dresden already runs an <b>active</b> webhook integration
    /// named "shared" — a real Keycloak client, not the disabled kind
    /// <c>HttpKeycloakAdminClient</c> replaces on re-registration (#2728). A
    /// munich operator tries to <b>create</b> their own webhook of the same
    /// name: munich's fab-scoped repository lookup (spec 182's AS-4/AS-9 fix)
    /// finds nothing under "webhook-shared" in munich, so the handler takes
    /// the create branch and asks Keycloak to mint "webhook-shared" — which
    /// Keycloak refuses because Dresden's client is already there, via
    /// <see cref="KeycloakClientAlreadyExistsException"/>.
    ///
    /// <para>
    /// Today that exception falls into the handler's generic catch and comes
    /// back as <c>502 KEYCLOAK_UNAVAILABLE</c> with "Keycloak client
    /// 'webhook-shared' already exists." as the message — telling munich's
    /// caller, who holds no access to Dresden's fab, that Dresden runs an
    /// integration by that name. The refusal must instead be a generic
    /// conflict that never names the clientId, mirroring
    /// <c>UniqueConstraintExceptionHandler</c>'s <c>409
    /// RESOURCE_ALREADY_EXISTS</c> — this codebase's existing answer for
    /// exactly this shape of "someone else already has this" collision —
    /// that handler deliberately echoes nothing from the colliding row, for
    /// the identical reason: a multi-fab deployment can collide on a value
    /// the caller cannot see.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_munich_create_colliding_with_dresdens_active_webhook_gets_a_generic_conflict_that_does_not_name_the_client()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient dresdenClient = RegisteredClient.Register(
            ClientId.From("webhook-shared"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("dresden"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(dresdenClient, version: 1);

        // Dresden's Keycloak client is planted directly, the same way
        // RotateWebhookClientCommandHandler's own create branch would have
        // minted it, so munich's attempt below collides with a real entry
        // rather than one the fake never heard of.
        FakeKeycloakAdminClient keycloak = new();
        await keycloak.CreateClientAsync(
            new KeycloakClientRepresentation(
                ClientId: "webhook-shared",
                Name: "Webhook shared",
                ServiceAccountsEnabled: true,
                StandardFlowEnabled: false,
                DirectAccessGrantsEnabled: false,
                PublicClient: false,
                DefaultClientScopes: [],
                OptionalClientScopes: [],
                Attributes: new Dictionary<string, string>
                {
                    ["sse.kind"] = "webhook",
                    ["sse.fab"] = "dresden",
                }),
            fabGroupPath: "/fabs/dresden",
            CancellationToken.None);
        string dresdenSecretBeforeAttempt = keycloak.CurrentSecrets["webhook-shared"];

        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeWebhookIntegrationStatusLookup(), bus,
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        // munich's own fab-scoped lookup finds nothing under "webhook-shared",
        // so this reads as the create intent (If-None-Match: *) to the handler.
        RotateWebhookClientCommand command = new(
            "shared", FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.None);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue(
            "munich must not be able to create a webhook client that collides with Dresden's");

        string errorText = $"{result.Error.Code} {result.Error.Message}";
        errorText.Contains("webhook-shared", StringComparison.Ordinal).ShouldBeFalse(
            $"the refusal must not name the clientId — a munich caller must not learn Dresden owns "
            + $"this name; got '{errorText}'");

        result.Error.Code.ShouldNotBe(
            "KEYCLOAK_UNAVAILABLE",
            "a same-name collision with another fab's client is a conflict the caller caused, not "
            + "Keycloak being unreachable");
        result.Error.Status.ShouldBe(
            HttpStatusCode.Conflict,
            $"this must be a generic 409 conflict — mirroring UniqueConstraintExceptionHandler's "
            + $"RESOURCE_ALREADY_EXISTS — not the 502 munich's caller gets today; got "
            + $"{result.Error.Status} ({result.Error.Code}: {result.Error.Message})");

        bus.Published.ShouldBeEmpty(
            "no WebhookIntegrationRotatedV1 may be published for a create that never happened");
        keycloak.CurrentSecrets["webhook-shared"].ShouldBe(
            dresdenSecretBeforeAttempt,
            "Dresden's live secret must be unchanged by munich's refused collision attempt");
    }

    // Spec 318 (#2628). The handler must ask IWebhookIntegrationStatusLookup
    // before doing anything else, and must refuse a Revoked or Unverifiable
    // answer regardless of which Layer-1 branch the caller's precondition
    // would otherwise have taken (plan §4).

    [Fact]
    public async Task A_revoked_integration_is_refused_on_a_create_intent_and_nothing_is_minted()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.Revoked };
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.WebhookIntegrationRevoked>();
        keycloak.Created.ShouldBeEmpty(
            "a revoked integration must never have a Keycloak client minted for it");
        repo.Clients.ShouldBeEmpty(
            "a revoked integration must never gain a registered_clients row");
        bus.Published.ShouldBeEmpty(
            "no WebhookIntegrationRotatedV1 may be published for a rotation that never happened");
    }

    /// <summary>
    /// Spec 318 §1's third mode: the asynchronous spec-264 disable has not
    /// landed yet, so the local row is still enabled at version V. A rotate
    /// intent (If-Match: V) must still be refused — the check runs before
    /// the rotate branch's <c>aggregate.Rotate(clock)</c> + <c>SaveAsync</c>,
    /// so neither the secret nor the row's version/LastRotatedAt moves.
    /// </summary>
    [Fact]
    public async Task A_revoked_integration_is_refused_on_a_rotate_intent_and_the_existing_row_is_untouched()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(client, version: 3);

        FakeKeycloakAdminClient keycloak = new();
        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.Revoked };
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, new FakeEventBus(), new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler.HandleAsync(
            new RotateWebhookClientCommand(
                "qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()),
                Option<int>.Some(3)),
            CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.WebhookIntegrationRevoked>();
        keycloak.CallCount.ShouldBe(
            0, "a revoked integration must not have RotateClientSecretAsync called for it");
        RegisteredClient stored = repo.Clients.ShouldHaveSingleItem();
        stored.Version.Value.ShouldBe(3, "the version must not move for a refused rotation");
        stored.LastRotatedAt.ShouldBeNull("LastRotatedAt must not move for a refused rotation");
    }

    /// <summary>
    /// The ordering spec §2's "conflict" scenario pins: the revoked check
    /// precedes ADR-0113 Layer 1, so a stale If-Match version answers Revoked,
    /// never WebhookClientStale.
    /// </summary>
    [Fact]
    public async Task A_revoked_integration_with_a_stale_version_answers_revoked_not_stale()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(client, version: 5);

        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.Revoked };
        RotateWebhookClientCommandHandler handler = new(
            repo, new FakeKeycloakAdminClient(), status, new FakeEventBus(),
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler.HandleAsync(
            new RotateWebhookClientCommand(
                "qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()),
                Option<int>.Some(3)), // stale: the row is really at 5
            CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.WebhookIntegrationRevoked>(
            $"got {result.Error}; a stale version on a revoked integration must still answer "
            + "revoked, not WebhookClientStale");
    }

    /// <summary>
    /// The same ordering, the other branch: a create intent (None) against a
    /// row that already exists must still answer Revoked, never
    /// WebhookClientAlreadyExists.
    /// </summary>
    [Fact]
    public async Task A_revoked_integration_with_a_create_intent_against_an_existing_row_answers_revoked_not_already_exists()
    {
        InMemoryRegisteredClientRepository repo = new();
        RegisteredClient client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(client, version: 1);

        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.Revoked };
        RotateWebhookClientCommandHandler handler = new(
            repo, new FakeKeycloakAdminClient(), status, new FakeEventBus(),
            new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None); // create intent

        result.Error.ShouldBeOfType<RotateWebhookClientError.WebhookIntegrationRevoked>(
            $"got {result.Error}; a create intent against a revoked integration's existing row must "
            + "still answer revoked, not WebhookClientAlreadyExists");
    }

    [Fact]
    public async Task An_unverifiable_integration_status_refuses_the_rotation_and_nothing_is_minted()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.Unverifiable };
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.WebhookIntegrationStatusUnavailable>();
        keycloak.Created.ShouldBeEmpty(
            "the rotation must fail closed when EventIngestion's state cannot be confirmed");
        repo.Clients.ShouldBeEmpty();
        bus.Published.ShouldBeEmpty();
    }

    /// <summary>Spec §3 "Not registered" — unchanged for this spec (tracked as a follow-up).</summary>
    [Fact]
    public async Task A_not_registered_integration_status_still_allows_the_first_rotation_to_succeed()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new() { Status = WebhookIntegrationStatus.NotRegistered };
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"a name EventIngestion has never registered in this fab must rotate exactly as today; "
            + $"got {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        repo.Clients.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_invalid_integration_name_is_refused_before_the_status_lookup_is_called()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler
            .HandleAsync(HappyCommand("Has Spaces"), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.InvalidIntegrationName>();
        status.Calls.ShouldBeEmpty(
            "a 400 must not cost an outbound call to EventIngestion (spec 318 §2)");
    }

    /// <summary>
    /// Spec 318 (#2628), today's create-branch TOCTOU scope expansion: a
    /// successful create asks the lookup <b>twice</b> with the same fab and
    /// name — once before either branch runs, and once more after the new
    /// row's own commit (<c>DisableIfRevokedSinceCommitAsync</c>) — rather
    /// than once. Both calls must still carry the command's own fab and
    /// integration name; this is not a relaxation of what the fact checks,
    /// only of how many times it expects to see it checked.
    /// </summary>
    [Fact]
    public async Task The_status_lookup_is_called_twice_with_the_commands_fab_and_integration_name()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            NoOpDisableWebhookClient(), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand("qa"), CancellationToken.None);

        status.Calls.Count.ShouldBe(
            2, "the create branch asks once before either branch runs and once more after its own "
            + "commit, to close the TOCTOU race (spec 318 scope expansion, #2628)");
        status.Calls.ShouldAllBe(call =>
            call.Fab == FabIdentifier.From("munich") && call.IntegrationName == "qa");
    }

    [Fact]
    public void The_revoked_error_is_a_409_naming_the_integration()
    {
        ApiError error = new RotateWebhookClientError.WebhookIntegrationRevoked("qa");

        error.Code.ShouldBe("WEBHOOK_INTEGRATION_REVOKED");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
        error.Message.ShouldContain("qa");
    }

    [Fact]
    public void The_status_unavailable_error_is_a_502_with_fixed_text()
    {
        ApiError error = new RotateWebhookClientError.WebhookIntegrationStatusUnavailable();

        error.Code.ShouldBe("WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE");
        error.Status.ShouldBe(HttpStatusCode.BadGateway);
    }

    // Phase 6 found the TOCTOU re-check's own behaviour was never actually
    // exercised: the old fake's single fixed Status could not express "the
    // pre-flight check passes, the post-commit re-check finds the race",
    // which is the entire point of DisableIfRevokedSinceCommitAsync (#2628).
    // FakeWebhookIntegrationStatusLookup.EnqueueStatuses now can.

    /// <summary>
    /// The documented happy path for the race itself: the create commits
    /// while the integration is still Active, a revoke lands before the
    /// post-commit re-check runs, and the handler disables the client it just
    /// minted rather than leaving a revoked integration's credential live.
    /// The create genuinely succeeded, so the caller still gets its secret
    /// back (200) — matching spec 264's existing async-disable precedent,
    /// where a disable racing a live request never turns that request's own
    /// answer into a failure.
    /// </summary>
    [Fact]
    public async Task A_revoke_landing_during_the_create_disables_the_just_created_client_and_still_returns_success()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        CapturingLogger<RotateWebhookClientCommandHandler> logger = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            DisableWebhookClientUsing(repo, keycloak), logger);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"the create itself committed before the revoke landed, so it must still be reported "
            + $"as success; got {(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        keycloak.Disabled.ShouldContain(
            "webhook-qa",
            "the race-created Keycloak client must be disabled once the post-commit re-check sees "
            + "Revoked");
        RegisteredClient stored = repo.Clients.ShouldHaveSingleItem();
        stored.DisabledAt.ShouldNotBeNull(
            "the local row must be marked disabled too, not just the Keycloak client");
        logger.Named("DisabledClientCreatedDuringRevokeRace").ShouldHaveSingleItem(
            "the race-disable marker log must fire so an operator can tell this happened");
        bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "a client this same call just disabled must not also be announced as rotated — that "
            + "pairs a rotated event with a client already turned off");
    }

    /// <summary>
    /// The disable itself is documented as best-effort: a failure to disable
    /// must not be surfaced to the caller, because the create genuinely
    /// succeeded. This pins that the handler still returns Success and logs a
    /// warning instead of letting the disable's own exception propagate.
    /// </summary>
    [Fact]
    public async Task When_the_race_disable_itself_fails_the_create_still_returns_success_and_a_warning_is_logged()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        CapturingLogger<RotateWebhookClientCommandHandler> logger = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            DisableWebhookClientUsing(repo, keycloak), logger);

        // The create's own CreateClientAsync must still succeed; only the
        // later, best-effort DisableClientAsync call fails.
        keycloak.FailNextDisableWith = new HttpRequestException("Keycloak 503 on disable");

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"a failed best-effort disable must not turn a committed create into a failure; got "
            + $"{(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        logger.Named("CouldNotDisableClientCreatedDuringRevokeRace").ShouldHaveSingleItem(
            "a failed race-disable must be logged so the still-enabled client is not silently lost "
            + "sight of");
        bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem(
            "a race-disable that genuinely failed leaves the client enabled, so the rotation must "
            + "still be announced");
    }

    /// <summary>
    /// Phase 6 should-fix S3 (#2628): today <c>DisableIfRevokedSinceCommitAsync</c>
    /// only acts on a post-commit <c>Revoked</c> answer and does nothing for
    /// <c>Unverifiable</c>, silently leaving the just-created client enabled
    /// when EventIngestion's state cannot be confirmed. The pre-flight check a
    /// few lines above already treats Unverifiable exactly like Revoked
    /// (fail-closed); the post-commit re-check must match it for the same
    /// reason — an integration this handler cannot confirm the health of is
    /// not one it should leave a live credential attached to. Red today: the
    /// current code returns early without disabling.
    /// </summary>
    [Fact]
    public async Task An_unverifiable_status_after_commit_disables_the_just_created_client_like_a_revoke()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Unverifiable);
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            DisableWebhookClientUsing(repo, keycloak), NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"the create itself committed, so it must still be reported as success; got "
            + $"{(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        keycloak.Disabled.ShouldContain(
            "webhook-qa",
            "an Unverifiable answer after commit must disable the just-created client exactly as a "
            + "Revoked answer does — the fail-closed philosophy applies to both");
        RegisteredClient stored = repo.Clients.ShouldHaveSingleItem();
        stored.DisabledAt.ShouldNotBeNull(
            "the local row must be marked disabled, not left enabled because the status could not "
            + "be confirmed");
    }

    /// <summary>
    /// Phase 6 should-fix S1 (#2628), the real bug: the async
    /// <c>WebhookIntegrationRevokedV1</c> disable handler can land concurrently
    /// with this same race window, disabling the row and bumping its version
    /// before <c>DisableIfRevokedSinceCommitAsync</c>'s own
    /// <c>aggregate.Disable(clock); SaveAsync</c> runs — so this save loses
    /// the identical Layer-2 race
    /// <c>A_rotation_that_loses_the_database_race_lets_the_concurrency_exception_reach_the_middleware</c>
    /// pins for the rotate branch's own save.
    ///
    /// <para>
    /// Today's exception filter
    /// (<c>when (ex is not OperationCanceledException and not DbUpdateConcurrencyException)</c>)
    /// does not catch <see cref="DbUpdateConcurrencyException"/> here either,
    /// so it propagates all the way to the caller — turning a create that
    /// genuinely committed into a 409, and losing the only copy of the
    /// secret. The correct behaviour: this is "already disabled concurrently"
    /// by the async handler, not a conflict the caller caused, so it must be
    /// caught, logged, and still answer Success. Red today: the exception
    /// propagates past the handler uncaught.
    /// </para>
    ///
    /// <para>
    /// A second review round found the first version of this test green for
    /// the wrong reason: it wired <see cref="NoOpTransactionalCommit"/>, which
    /// has no relationship to <paramref name="repo"/> and so cannot see that
    /// the outer <c>HandleAsync</c> still unconditionally calls
    /// <c>commit.CommitAsync</c> after the inner save's concurrency exception
    /// is caught — in production that is the SAME <c>DbContext</c> the failed
    /// save left a stale, Modified entity tracked on, and the second commit
    /// replays the identical failing UPDATE uncaught. This version wires
    /// <see cref="RepositoryBackedTransactionalCommit"/> instead, which can
    /// see exactly that.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_concurrent_async_disable_during_the_race_check_is_treated_as_already_disabled_not_a_409()
    {
        InMemoryRegisteredClientRepository repo = new();
        repo.FailSecondSaveWith = new DbUpdateConcurrencyException(
            "The async WebhookIntegrationRevokedV1 disable handler already disabled this row and "
            + "bumped its version first.");
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        CapturingLogger<RotateWebhookClientCommandHandler> logger = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new RepositoryBackedTransactionalCommit(repo), new FakeClock(Now),
            DisableWebhookClientUsing(repo, keycloak), logger);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"a concurrency loss on the best-effort race-disable's own save means the async handler "
            + $"already disabled this row — that is not a failure of this create, which genuinely "
            + $"committed, and must not cost the caller its secret; got "
            + $"{(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "a client the async handler already disabled must not also be announced as rotated");
    }

    /// <summary>
    /// Phase 6 should-fix SF1 (#2628): the asynchronous
    /// <c>WebhookIntegrationRevokedV1</c> disable can also beat this re-check
    /// to the punch without a save conflict at all — its own save commits
    /// first, and <c>DisableWebhookClientCommandHandler</c>'s fab-scoped
    /// lookup excludes Disabled rows at the database level, so it finds
    /// nothing and returns <c>WebhookClientNotFound</c> rather than throwing.
    /// Before this fix, any failure from that inner call — including
    /// <c>WebhookClientNotFound</c> — was treated as "could not disable",
    /// which both logs a false "manual intervention needed" warning and (via
    /// S2's gating) publishes <see cref="WebhookIntegrationRotatedV1"/> for a
    /// client that is already disabled: a false alarm plus the exact
    /// incoherent audit state S2 was meant to prevent.
    /// </summary>
    [Fact]
    public async Task A_WebhookClientNotFound_from_the_inner_disable_is_treated_as_already_disabled_not_a_failure()
    {
        InMemoryRegisteredClientRepository repo = new();
        repo.DisableRowOnSecondGetWithinFab = new FakeClock(Now);
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        CapturingLogger<RotateWebhookClientCommandHandler> logger = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, status, bus, new NoOpTransactionalCommit(), new FakeClock(Now),
            DisableWebhookClientUsing(repo, keycloak), logger);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            $"the async handler already disabled this row before the inner lookup ran — that is not "
            + $"a failure of this create, which genuinely committed; got "
            + $"{(result.IsFailure ? result.Error.ToString() : string.Empty)}");
        bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "a client the async handler already disabled must not also be announced as rotated");
        logger.Named("AlreadyDisabledConcurrentlyDuringRevokeRace").ShouldHaveSingleItem(
            "a WebhookClientNotFound from the inner disable must be read as 'already disabled', not "
            + "logged as a could-not-disable failure");
    }
}
