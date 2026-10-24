using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// Spec 434 (#3080). A client disconnect after the create branch's row has
/// committed must not skip the compensation that follows it: the race
/// disable, its retry message, the rotated announcement and the outbox flush.
/// Every collaborator honours its token (<see cref="TokenHonouringPorts"/>'s
/// wrappers), as the real ones do; against the in-memory fakes alone a handler
/// that keeps using the cancelled request token would pass.
/// </summary>
public class RotateWebhookClientCancellationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-22T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static RotateWebhookClientCommand CreateCommand() =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.None);

    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource request = new();

        public Harness(CancelPoint cancelAt, params WebhookIntegrationStatus[] statuses)
        {
            Status.EnqueueStatuses(statuses);
            Repository = new TokenHonouringRegisteredClientRepository(Inner, request, cancelAt);
            Keycloak = new TokenHonouringKeycloakAdminClient(InnerKeycloak);
            DisableHandler = new DisableWebhookClientCommandHandler(
                Repository, Keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance);
            Handler = new RotateWebhookClientCommandHandler(
                Repository,
                Keycloak,
                new TokenHonouringWebhookIntegrationStatusLookup(Status),
                new TokenHonouringEventBus(Bus),
                new TokenHonouringTransactionalCommit(Commit),
                new FakeClock(Now),
                DisableHandler,
                Time,
                NullLogger<RotateWebhookClientCommandHandler>.Instance);
        }

        public InMemoryRegisteredClientRepository Inner { get; } = new();

        public FakeKeycloakAdminClient InnerKeycloak { get; } = new();

        public FakeWebhookIntegrationStatusLookup Status { get; } = new();

        public FakeEventBus Bus { get; } = new();

        public NoOpTransactionalCommit Commit { get; } = new();

        public ManualTimeProvider Time { get; } = new(Now);

        public TokenHonouringRegisteredClientRepository Repository { get; }

        public TokenHonouringKeycloakAdminClient Keycloak { get; }

        public DisableWebhookClientCommandHandler DisableHandler { get; }

        public RotateWebhookClientCommandHandler Handler { get; }

        public CancellationToken RequestToken => request.Token;

        public void Dispose() => request.Dispose();
    }

    [Fact]
    public async Task A_disconnect_after_the_create_commits_still_disables_a_client_revoked_in_the_race()
    {
        using Harness harness = new(
            CancelPoint.AfterFirstSave, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);

        var failure = await Record.ExceptionAsync(
            () => harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken).WaitAsync(TestTimeout));

        failure.ShouldBeNull("the create committed, so the handler must finish its compensation and return success");
        harness.InnerKeycloak.Disabled.ShouldContain("webhook-qa");
        harness.Inner.Clients.ShouldHaveSingleItem().DisabledAt.ShouldNotBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "a client this same call disabled must not be announced as rotated");
    }

    [Fact]
    public async Task A_disconnect_after_the_create_commits_still_publishes_the_retry_message_when_the_race_disable_fails()
    {
        using Harness harness = new(
            CancelPoint.AfterFirstSave, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.InnerKeycloak.FailNextDisableWith = new HttpRequestException("Keycloak 503");

        var failure = await Record.ExceptionAsync(
            () => harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken).WaitAsync(TestTimeout));

        failure.ShouldBeNull();
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem()
            .IntegrationName.ShouldBe("qa");
        harness.Commit.Commits.ShouldBe(1, "the outbox flush that releases the retry message must still run");
    }

    [Fact]
    public async Task A_disconnect_after_the_create_commits_still_announces_the_rotation_of_an_active_integration()
    {
        using Harness harness = new(
            CancelPoint.AfterFirstSave, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Active);

        var failure = await Record.ExceptionAsync(
            () => harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken).WaitAsync(TestTimeout));

        failure.ShouldBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        harness.Commit.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_race_disable_that_outlasts_the_compensation_bound_is_treated_as_a_failed_disable()
    {
        using Harness harness = new(
            CancelPoint.AfterFirstSave, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.Keycloak.DisableNeverAnswers = true;

        var handling = harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken);
        await Task.WhenAny(handling, harness.Time.WaitForPendingTimersAsync(1, TestTimeout));
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var failure = await Record.ExceptionAsync(() => handling.WaitAsync(TestTimeout));

        failure.ShouldBeNull("an expired bound is a failed disable, not an unhandled cancellation");
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem();
        harness.Commit.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_bound_that_expires_while_the_disabled_row_is_saving_does_not_announce_a_rotation()
    {
        using Harness harness = new(
            CancelPoint.Never, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.Repository.SaveNeverAnswersOnCall = 2;

        var handling = harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken);
        await Task.WhenAny(handling, harness.Time.WaitForPendingTimersAsync(1, TestTimeout));
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var failure = await Record.ExceptionAsync(() => handling.WaitAsync(TestTimeout));

        failure.ShouldBeNull();
        harness.Inner.Clients.ShouldHaveSingleItem().DisabledAt.ShouldNotBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "the later commit saves the disabled row, so announcing a rotation for it is incoherent");
        harness.Commit.Commits.ShouldBe(1, "the disabled row still has to be saved");
    }

    [Fact]
    public async Task A_bound_that_expires_while_the_disabled_row_is_saving_also_publishes_the_idempotent_retry()
    {
        using Harness harness = new(
            CancelPoint.Never, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.Repository.SaveNeverAnswersOnCall = 2;

        var handling = harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken);
        await Task.WhenAny(handling, harness.Time.WaitForPendingTimersAsync(1, TestTimeout));
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var failure = await Record.ExceptionAsync(() => handling.WaitAsync(TestTimeout));

        failure.ShouldBeNull();
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem()
            .IntegrationName.ShouldBe("qa");
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty();
        harness.Commit.Commits.ShouldBe(1, "one commit saves the row and flushes the retry together");
    }

    [Fact]
    public async Task A_cancellation_inside_the_disable_while_the_bound_is_still_running_is_a_failed_disable()
    {
        using Harness harness = new(
            CancelPoint.Never, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        harness.InnerKeycloak.FailNextDisableWith = new TaskCanceledException("HttpClient.Timeout");

        var failure = await Record.ExceptionAsync(
            () => harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken).WaitAsync(TestTimeout));

        failure.ShouldBeNull("an internal timeout after the commit must not become a 500");
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem();
        harness.Commit.Commits.ShouldBe(1);
    }

    /// <summary>
    /// Spec 451 FR-003: on the existing-client path a status re-check that cannot
    /// be confirmed must not disable the live client. An expired bound is such a
    /// case; today its cancellation reaches the generic cancellation catch, which
    /// publishes the race-disable retry that later disables the live credential.
    /// </summary>
    [Fact]
    public async Task A_bound_that_expires_during_the_rotate_recheck_leaves_the_live_client_enabled()
    {
        InMemoryRegisteredClientRepository repository = new();
        var client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        repository.Seed(client, version: 3);
        FakeKeycloakAdminClient keycloak = new();
        await keycloak.CreateClientAsync(
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
        SecondCallNeverAnswersStatusLookup status = new();
        FakeEventBus bus = new();
        NoOpTransactionalCommit commit = new();
        ManualTimeProvider time = new(Now);
        CapturingLogger<RotateWebhookClientCommandHandler> logger = new();
        DisableWebhookClientCommandHandler disableHandler = new(
            repository, keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance);
        RotateWebhookClientCommandHandler handler = new(
            repository, keycloak, status, bus, commit, new FakeClock(Now), disableHandler, time, logger);
        RotateWebhookClientCommand command = new(
            "qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.Some(3));

        var handling = handler.HandleAsync(command, CancellationToken.None);
        await Task.WhenAny(handling, time.WaitForPendingTimersAsync(1, TestTimeout));
        time.Advance(TimeSpan.FromSeconds(31));

        var result = await handling.WaitAsync(TestTimeout);

        result.IsSuccess.ShouldBeTrue("the rotation committed, so the call answers success: " + (result.IsFailure ? result.Error : string.Empty));
        bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldBeEmpty(
            "an unconfirmable re-check on a rotated live client must not queue a disable");
        repository.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
        keycloak.Disabled.ShouldBeEmpty();
        bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        commit.Commits.ShouldBe(1);
        logger.Named("RotationRecheckStatusUnverifiable").ShouldHaveSingleItem();
    }

    private static async Task SeedExistingClientAsync(Harness harness)
    {
        var client = RegisteredClient.Register(
            ClientId.From("webhook-qa"),
            ClientKind.WebhookIntegration,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FakeClock(Now));
        harness.Inner.Seed(client, version: 3);
        await harness.InnerKeycloak.CreateClientAsync(
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
    }

    private static RotateWebhookClientCommand CreateRotateCommand() =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.Some(3));

    [Fact]
    public async Task A_bound_that_expires_during_the_rotate_disable_of_a_revoked_client_is_a_failed_disable()
    {
        using Harness harness = new(
            CancelPoint.Never, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        await SeedExistingClientAsync(harness);
        harness.Keycloak.DisableNeverAnswers = true;

        var handling = harness.Handler.HandleAsync(CreateRotateCommand(), harness.RequestToken);
        await Task.WhenAny(handling, harness.Time.WaitForPendingTimersAsync(1, TestTimeout));
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var result = await handling.WaitAsync(TestTimeout);

        result.IsSuccess.ShouldBeTrue("the rotation committed, so the call answers success");
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem()
            .IntegrationName.ShouldBe("qa");
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        harness.InnerKeycloak.Disabled.ShouldNotContain("webhook-qa");
        harness.Inner.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
        harness.Commit.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_bound_that_expires_while_the_rotate_disabled_row_is_saving_does_not_announce_a_rotation()
    {
        using Harness harness = new(
            CancelPoint.Never, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        await SeedExistingClientAsync(harness);
        harness.Repository.SaveNeverAnswersOnCall = 2;

        var handling = harness.Handler.HandleAsync(CreateRotateCommand(), harness.RequestToken);
        await Task.WhenAny(handling, harness.Time.WaitForPendingTimersAsync(1, TestTimeout));
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var result = await handling.WaitAsync(TestTimeout);

        result.IsSuccess.ShouldBeTrue("the rotation committed, so the call answers success");
        harness.InnerKeycloak.Disabled.ShouldContain("webhook-qa");
        harness.Inner.Clients.ShouldHaveSingleItem().DisabledAt.ShouldNotBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty(
            "the later commit saves the disabled row, so announcing a rotation for it is incoherent");
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem();
        harness.Commit.Commits.ShouldBe(1, "one commit saves the row and flushes the retry together");
    }

    private sealed class SecondCallNeverAnswersStatusLookup : IWebhookIntegrationStatusLookup
    {
        private int calls;

        public async Task<WebhookIntegrationStatus> GetStatusAsync(
            FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++calls > 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return WebhookIntegrationStatus.Active;
        }
    }

    [Fact]
    public async Task A_disconnect_before_the_create_commits_still_propagates_and_commits_nothing()
    {
        using Harness harness = new(
            CancelPoint.BeforeFirstSave, WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Active);

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Handler.HandleAsync(CreateCommand(), harness.RequestToken));

        harness.Commit.Commits.ShouldBe(0);
        harness.Bus.Published.ShouldBeEmpty();
    }
}
