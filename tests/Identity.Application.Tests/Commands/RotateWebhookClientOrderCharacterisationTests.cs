using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on the unmodified
/// <c>RotateWebhookClientCommandHandler</c> before <c>HandleAsync</c> and
/// <c>DisableIfRevokedSinceCommitAsync</c> are split into private methods. The
/// handler turns a webhook integration's bearer into a live Keycloak
/// credential, so what is pinned here is the order it reaches its ports in
/// (one shared call log over every port, <c>RecordingIdentityPorts</c>
/// decorators), the exact extent of its <c>try</c> and the filters of its
/// <c>catch</c> clauses, and the text a log reader sees. Existing tests pin the
/// outcomes; none of them can see an order.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the pre-flight status
/// lookup below the fab-scoped repository lookup turns
/// <see cref="A_revoked_status_is_refused_after_one_status_call_and_before_the_fab_scoped_lookup"/>
/// red (the log gains a leading "lookup"); swapping the rotate branch's
/// <c>SaveAsync</c> and <c>RotateClientSecretAsync</c> turns
/// <see cref="The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits"/> red.
/// </para>
///
/// <para>
/// Expected values are literals written from reading the unrefactored
/// handler. Spec 356 then changed one of them on purpose: a save failure that
/// is not a concurrency conflict now propagates instead of being answered 502
/// KEYCLOAK_UNAVAILABLE although Keycloak was never called.
/// </para>
/// </summary>
public class RotateWebhookClientOrderCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static RotateWebhookClientCommand CreateIntent() =>
        new("qa", Munich, Operator, Option<int>.None);

    private static RotateWebhookClientCommand RotateIntent(int version) =>
        new("qa", Munich, Operator, Option<int>.Some(version));

    private sealed class Harness
    {
        public Harness(
            Func<Harness, ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>>? disable = null,
            bool repositoryBackedCommit = false)
        {
            Keycloak = new RecordingKeycloakAdminClient(InnerKeycloak, Log);
            RecordingRegisteredClientRepository recordingRepository = new(Repository, Log);
            Disable = disable?.Invoke(this)
                ?? new DisableWebhookClientCommandHandler(
                    recordingRepository,
                    Keycloak,
                    new FakeClock(Now),
                    NullLogger<DisableWebhookClientCommandHandler>.Instance);
            ITransactionalCommit innerCommit = repositoryBackedCommit
                ? new RepositoryBackedTransactionalCommit(Repository)
                : new NoOpTransactionalCommit();
            Handler = new RotateWebhookClientCommandHandler(
                recordingRepository,
                Keycloak,
                new RecordingWebhookIntegrationStatusLookup(Status, Log),
                new RecordingEventBus(Bus, Log),
                new RecordingTransactionalCommit(innerCommit, Log),
                new FakeClock(Now),
                Disable,
                Logger);
        }

        public List<string> Log { get; } = [];

        public InMemoryRegisteredClientRepository Repository { get; } = new();

        public FakeKeycloakAdminClient InnerKeycloak { get; } = new();

        public RecordingKeycloakAdminClient Keycloak { get; }

        public FakeWebhookIntegrationStatusLookup Status { get; } = new();

        public FakeEventBus Bus { get; } = new();

        public CapturingLogger<RotateWebhookClientCommandHandler> Logger { get; } = new();

        public ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>> Disable { get; }

        public RotateWebhookClientCommandHandler Handler { get; }

        public async Task SeedRotatableClientAsync(int version)
        {
            var client = RegisteredClientAggregate.Register(
                ClientId.From("webhook-qa"),
                ClientKind.WebhookIntegration,
                Munich,
                Operator,
                new FakeClock(Now));
            Repository.Seed(client, version);
            await InnerKeycloak.CreateClientAsync(
                new KeycloakClientRepresentation(
                    ClientId: "webhook-qa",
                    Name: "Webhook qa",
                    ServiceAccountsEnabled: true,
                    StandardFlowEnabled: false,
                    DirectAccessGrantsEnabled: false,
                    PublicClient: false,
                    DefaultClientScopes: [],
                    OptionalClientScopes: [],
                    Attributes: new Dictionary<string, string>()),
                fabGroupPath: "/fabs/munich",
                CancellationToken.None);
            Log.Clear();
        }

        public Task<Result<SmartSentinelEye.Identity.Application.DTOs.WebhookClientCredentialsDto, RotateWebhookClientError>> RunAsync(
            RotateWebhookClientCommand command) =>
            Handler.HandleAsync(command, CancellationToken.None);
    }

    private sealed class ScriptedDisableHandler(
        List<string> log,
        Func<Task<Result<RegisteredClientIdentifier, DisableWebhookClientError>>> script)
        : ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>
    {
        public Task<Result<RegisteredClientIdentifier, DisableWebhookClientError>> HandleAsync(
            DisableWebhookClientCommand command, CancellationToken cancellationToken)
        {
            log.Add("disable-command");
            return script();
        }
    }

    // ---- order across ports -------------------------------------------------

    [Fact]
    public async Task A_revoked_status_is_refused_after_one_status_call_and_before_the_fab_scoped_lookup()
    {
        Harness harness = new();
        harness.Status.Status = WebhookIntegrationStatus.Revoked;

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(["status"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookIntegrationRevoked("qa"));
        var entry = harness.Logger.Named("RefusedRotationOfRevokedIntegration").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("Refused rotation of revoked webhook integration 'qa' in fab munich.");
    }

    [Fact]
    public async Task An_unverifiable_status_is_refused_after_one_status_call_and_before_the_fab_scoped_lookup()
    {
        Harness harness = new();
        harness.Status.Status = WebhookIntegrationStatus.Unverifiable;

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(["status"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookIntegrationStatusUnavailable());
        var entry = harness.Logger.Named("RefusedRotationStatusUnavailable").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "Refused rotation of webhook integration 'qa' in fab munich: its revocation status could not be confirmed.");
    }

    [Fact]
    public async Task A_create_intent_against_an_existing_client_is_refused_before_any_write()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(["status", "lookup"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookClientAlreadyExists("webhook-qa", 3));
    }

    [Fact]
    public async Task A_rotate_intent_against_a_missing_client_is_refused_before_any_write()
    {
        Harness harness = new();

        var result = await harness.RunAsync(RotateIntent(version: 3));

        harness.Log.ShouldBe(["status", "lookup"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookClientNotFound("webhook-qa", 3));
    }

    [Fact]
    public async Task A_stale_version_is_refused_before_any_write()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 5);

        var result = await harness.RunAsync(RotateIntent(version: 3));

        harness.Log.ShouldBe(["status", "lookup"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookClientStale("webhook-qa", 3, 5));
        harness.Repository.Clients.ShouldHaveSingleItem().LastRotatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);

        var result = await harness.RunAsync(RotateIntent(version: 3));

        harness.Log.ShouldBe(["status", "lookup", "save", "rotate-secret", "publish", "commit"]);
        harness.Status.Calls.Count.ShouldBe(1);
        harness.Keycloak.RotateRequests.ShouldBe(["webhook-qa"]);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(4);
        result.Value.ClientId.ShouldBe("webhook-qa");
        result.Value.IntegrationName.ShouldBe("qa");
        result.Value.Fab.ShouldBe("munich");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa-rotated");
        result.Value.RegisteredClientIdentifier.ShouldBe(harness.Repository.Clients.Single().Id.Value);
        var entry = harness.Logger.Named("RotatedWebhookIntegration").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("Rotated webhook integration 'qa' to clientId 'webhook-qa'.");
    }

    [Fact]
    public async Task The_create_branch_creates_in_keycloak_then_adds_then_saves_then_rechecks_then_publishes_then_commits()
    {
        Harness harness = new();

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(
            ["status", "lookup", "create", "add", "save", "status", "publish", "commit"]);
        harness.Status.Calls.Count.ShouldBe(2);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(0);
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        var published = harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        published.IntegrationName.ShouldBe("qa");
        published.ClientId.ShouldBe("webhook-qa");
        published.RotatedAt.ShouldBe(Now);
        var entry = harness.Logger.Named("RotatedWebhookIntegration").ShouldHaveSingleItem();
        entry.Message.ShouldBe("Rotated webhook integration 'qa' to clientId 'webhook-qa'.");
    }

    [Fact]
    public async Task The_create_branch_sends_keycloak_exactly_this_representation_and_fab_group_path()
    {
        Harness harness = new();

        await harness.RunAsync(CreateIntent());

        var (representation, fabGroupPath) = harness.Keycloak.CreateRequests.ShouldHaveSingleItem();
        fabGroupPath.ShouldBe("/fabs/munich");
        representation.ClientId.ShouldBe("webhook-qa");
        representation.Name.ShouldBe("Webhook qa");
        representation.ServiceAccountsEnabled.ShouldBeTrue();
        representation.StandardFlowEnabled.ShouldBeFalse();
        representation.DirectAccessGrantsEnabled.ShouldBeFalse();
        representation.PublicClient.ShouldBeFalse();
        representation.DefaultClientScopes.ShouldBe(["sse.events.write", "sse-audience", "sse-groups"]);
        representation.OptionalClientScopes.ShouldBeEmpty();
        representation.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray().ShouldBe(
            [
                new KeyValuePair<string, string>("sse.fab", "munich"),
                new KeyValuePair<string, string>("sse.integrationName", "qa"),
                new KeyValuePair<string, string>("sse.kind", "webhook"),
            ]);
    }

    // ---- the create-branch revoke race ---------------------------------------

    [Fact]
    public async Task A_revoke_landing_during_the_create_disables_the_client_and_neither_publishes_nor_commits()
    {
        Harness harness = new();
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(
            [
                "status", "lookup", "create", "add", "save",
                "status", "lookup", "disable-client", "save",
            ]);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        harness.Bus.Published.ShouldBeEmpty();
        var disabled = harness.Logger.Named("DisabledClientCreatedDuringRevokeRace").ShouldHaveSingleItem();
        disabled.Level.ShouldBe(LogLevel.Information);
        disabled.Message.ShouldBe(
            "Disabled webhook client 'webhook-qa' for integration 'qa' immediately after creating it: "
            + "its revocation status changed while it was being created.");
        harness.Logger.Named("RotatedWebhookIntegration").ShouldHaveSingleItem().Message.ShouldBe(
            "Rotated webhook integration 'qa' to clientId 'webhook-qa'.");
    }

    [Fact]
    public async Task An_unverifiable_status_after_the_create_is_treated_like_a_revoke_and_skips_publish_and_commit()
    {
        Harness harness = new();
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Unverifiable);

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(
            [
                "status", "lookup", "create", "add", "save",
                "status", "lookup", "disable-client", "save",
            ]);
        result.IsSuccess.ShouldBeTrue();
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rotation_of_an_existing_client_never_rechecks_the_status_after_its_save()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 1);
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(RotateIntent(version: 1));

        result.IsSuccess.ShouldBeTrue();
        harness.Status.Calls.Count.ShouldBe(1);
        harness.Log.ShouldBe(["status", "lookup", "save", "rotate-secret", "publish", "commit"]);
    }

    [Fact]
    public async Task A_failed_race_disable_is_logged_with_this_exact_exception_message_and_the_rotation_is_still_announced()
    {
        Harness harness = new();
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.InnerKeycloak.FailNextDisableWith = new HttpRequestException("Keycloak 503 on disable");

        var result = await harness.RunAsync(CreateIntent());

        result.IsSuccess.ShouldBeTrue();
        harness.Log.ShouldBe(
            [
                "status", "lookup", "create", "add", "save",
                "status", "lookup", "disable-client",
                "publish", "publish", "commit",
            ]);
        var entry = harness.Logger.Named("CouldNotDisableClientCreatedDuringRevokeRace").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(
            "DisableWebhookClientCommand failed for 'qa': KEYCLOAK_UNAVAILABLE");
        entry.Message.ShouldBe(
            "Webhook client 'webhook-qa' for integration 'qa' was created while a revoke was landing, and could "
            + "not be disabled afterwards. It may still be enabled; the next rotation attempt or manual "
            + "intervention is needed.");
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_not_found_answer_from_the_inner_disable_is_read_as_already_disabled_and_skips_publish_and_commit()
    {
        Harness harness = new(h => new ScriptedDisableHandler(
            h.Log,
            () => Task.FromResult(
                Result<RegisteredClientIdentifier, DisableWebhookClientError>.Failure(
                    DisableWebhookClientFailures.WebhookClientNotFound("webhook-qa")))));
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(CreateIntent());

        result.IsSuccess.ShouldBeTrue();
        harness.Log.ShouldBe(
            ["status", "lookup", "create", "add", "save", "status", "disable-command"]);
        var entry = harness.Logger.Named("AlreadyDisabledConcurrentlyDuringRevokeRace").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(
            "Webhook client 'webhook-qa' for integration 'qa' was already disabled by the asynchronous "
            + "WebhookIntegrationRevokedV1 handler before this create-branch race check's own disable could land.");
    }

    [Fact]
    public async Task A_concurrency_conflict_from_the_inner_disable_is_read_as_already_disabled_not_as_a_failure()
    {
        Harness harness = new(h => new ScriptedDisableHandler(
            h.Log,
            () => throw new DbUpdateConcurrencyException("the async handler won the race")));
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(CreateIntent());

        result.IsSuccess.ShouldBeTrue();
        harness.Log.ShouldBe(
            ["status", "lookup", "create", "add", "save", "status", "disable-command"]);
        harness.Logger.Named("AlreadyDisabledConcurrentlyDuringRevokeRace").ShouldHaveSingleItem();
        harness.Logger.Named("CouldNotDisableClientCreatedDuringRevokeRace").ShouldBeEmpty();
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Any_other_exception_from_the_inner_disable_is_logged_as_not_disabled_and_the_rotation_is_announced()
    {
        var failure = new TimeoutException("disable timed out");
        Harness harness = new(h => new ScriptedDisableHandler(
            h.Log,
            () => throw failure));
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(CreateIntent());

        result.IsSuccess.ShouldBeTrue();
        harness.Log.ShouldBe(
            [
                "status", "lookup", "create", "add", "save",
                "status", "disable-command",
                "publish", "publish", "commit",
            ]);
        var entry = harness.Logger.Named("CouldNotDisableClientCreatedDuringRevokeRace").ShouldHaveSingleItem();
        entry.Exception.ShouldBeSameAs(failure);
        harness.Logger.Named("AlreadyDisabledConcurrentlyDuringRevokeRace").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_inside_the_inner_disable_propagates()
    {
        Harness harness = new(h => new ScriptedDisableHandler(
            h.Log,
            () => throw new OperationCanceledException("cancelled inside the disable")));
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.RunAsync(CreateIntent()));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
        harness.Bus.Published.ShouldBeEmpty();
        harness.Log.ShouldBe(
            ["status", "lookup", "create", "add", "save", "status", "disable-command"]);
    }

    [Fact]
    public async Task A_concurrent_disable_leaves_the_repository_backed_commit_unrun()
    {
        Harness harness = new(repositoryBackedCommit: true);
        harness.Repository.FailSecondSaveWith = new DbUpdateConcurrencyException("the async handler won");
        harness.Status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var result = await harness.RunAsync(CreateIntent());

        result.IsSuccess.ShouldBeTrue();
        harness.Log.ShouldBe(
            [
                "status", "lookup", "create", "add", "save",
                "status", "lookup", "disable-client", "save",
            ]);
        harness.Logger.Named("AlreadyDisabledConcurrentlyDuringRevokeRace").ShouldHaveSingleItem();
    }

    // ---- extent of the try and the catch filters -----------------------------

    [Fact]
    public async Task A_keycloak_name_conflict_on_create_is_a_generic_conflict_and_nothing_is_added_or_saved()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new KeycloakClientAlreadyExistsException("webhook-qa");

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(["status", "lookup", "create"]);
        result.Error.ShouldBe(RotateWebhookClientFailures.WebhookClientNameConflict());
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transport_failure_on_create_answers_keycloak_unavailable_with_the_exception_message()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new HttpRequestException("connection refused");

        var result = await harness.RunAsync(CreateIntent());

        harness.Log.ShouldBe(["status", "lookup", "create"]);
        result.Error.Code.ShouldBe("KEYCLOAK_UNAVAILABLE");
        result.Error.Message.ShouldBe("Keycloak Admin API call failed: connection refused");
    }

    [Fact]
    public async Task An_invalid_operation_from_keycloak_on_create_propagates_unconverted()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new InvalidOperationException("no client visible after POST");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => harness.RunAsync(CreateIntent()));

        thrown.Message.ShouldBe("no client visible after POST");
        harness.Log.ShouldBe(["status", "lookup", "create"]);
    }

    [Fact]
    public async Task A_cancellation_from_keycloak_on_create_propagates_unconverted()
    {
        Harness harness = new();
        harness.Keycloak.CreateThrows = new OperationCanceledException("cancelled in create");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.RunAsync(CreateIntent()));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_transport_failure_rolling_the_secret_answers_keycloak_unavailable_after_the_save()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Keycloak.RotateThrows = new HttpRequestException("rotate refused");

        var result = await harness.RunAsync(RotateIntent(version: 3));

        harness.Log.ShouldBe(["status", "lookup", "save", "rotate-secret"]);
        result.Error.Code.ShouldBe("KEYCLOAK_UNAVAILABLE");
        result.Error.Message.ShouldBe("Keycloak Admin API call failed: rotate refused");
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invalid_operation_from_keycloak_rolling_the_secret_propagates_unconverted()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Keycloak.RotateThrows = new InvalidOperationException("empty rotate response");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => harness.RunAsync(RotateIntent(version: 3)));

        thrown.Message.ShouldBe("empty rotate response");
    }

    [Fact]
    public async Task A_cancellation_from_keycloak_rolling_the_secret_propagates_unconverted()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Keycloak.RotateThrows = new OperationCanceledException("cancelled in rotate");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.RunAsync(RotateIntent(version: 3)));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_missing_keycloak_client_on_rotate_answers_keycloak_unavailable_with_the_not_found_message()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Keycloak.RotateThrows = new KeycloakClientNotFoundException("webhook-qa");

        var result = await harness.RunAsync(RotateIntent(version: 3));

        result.Error.Code.ShouldBe("KEYCLOAK_UNAVAILABLE");
        result.Error.Message.ShouldBe("Keycloak Admin API call failed: Keycloak client 'webhook-qa' not found.");
    }

    [Fact]
    public async Task A_concurrency_loss_saving_a_rotation_propagates_and_never_calls_keycloak()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Repository.FailNextSaveWith = new DbUpdateConcurrencyException("layer two loser");

        var thrown = await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => harness.RunAsync(RotateIntent(version: 3)));

        thrown.Message.ShouldBe("layer two loser");
        harness.Log.ShouldBe(["status", "lookup", "save"]);
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_concurrency_loss_saving_a_creation_propagates_after_keycloak_created_the_client()
    {
        Harness harness = new();
        harness.Repository.FailNextSaveWith = new DbUpdateConcurrencyException("create save lost");

        var thrown = await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => harness.RunAsync(CreateIntent()));

        thrown.Message.ShouldBe("create save lost");
        harness.Log.ShouldBe(["status", "lookup", "create", "add", "save"]);
    }

    // Spec 356: the rotate branch's save is not a Keycloak call, so a save
    // failure that is not a concurrency conflict propagates rather than being
    // reported as Keycloak being unavailable.
    [Fact]
    public async Task A_non_concurrency_save_failure_on_rotate_propagates_and_never_calls_keycloak()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Repository.FailNextSaveWith = new DbUpdateException("database unavailable");

        await Should.ThrowAsync<DbUpdateException>(
            () => harness.RunAsync(RotateIntent(version: 3)));

        harness.Log.ShouldBe(["status", "lookup", "save"]);
        harness.Keycloak.RotateRequests.ShouldBeEmpty();
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_failure_that_is_not_an_ef_exception_on_rotate_propagates_and_never_calls_keycloak()
    {
        Harness harness = new();
        await harness.SeedRotatableClientAsync(version: 3);
        harness.Repository.FailNextSaveWith = new TimeoutException("database unavailable");

        await Should.ThrowAsync<TimeoutException>(
            () => harness.RunAsync(RotateIntent(version: 3)));

        harness.Log.ShouldBe(["status", "lookup", "save"]);
        harness.Keycloak.RotateRequests.ShouldBeEmpty();
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_non_concurrency_save_failure_on_create_propagates_after_keycloak_created_the_client()
    {
        Harness harness = new();
        harness.Repository.FailNextSaveWith = new DbUpdateException("database unavailable");

        await Should.ThrowAsync<DbUpdateException>(
            () => harness.RunAsync(CreateIntent()));

        harness.Log.ShouldBe(["status", "lookup", "create", "add", "save"]);
        harness.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invalid_integration_name_is_refused_before_any_port_is_called()
    {
        Harness harness = new();

        var result = await harness.RunAsync(new RotateWebhookClientCommand("has space", Munich, Operator, Option<int>.None));

        harness.Log.ShouldBeEmpty();
        result.Error.Code.ShouldBe("WEBHOOK_INVALID_INPUT");
        result.Error.Message.ShouldBe(
            "value: must start with a letter or digit and contain only letters, digits, or '.', '_', '-' (Parameter 'value')");
    }
}
