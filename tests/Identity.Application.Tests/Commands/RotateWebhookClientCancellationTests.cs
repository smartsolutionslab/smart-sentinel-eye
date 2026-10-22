using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.EventHandlers;
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
