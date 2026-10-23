using System.Globalization;
using Microsoft.EntityFrameworkCore;
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
/// Characterisation for ADR-0185 (plan risk R2). <see cref="WebhookClientRaceDisableFailed"/> is
/// declared durable, so its publish from the failure branches of
/// <see cref="RotateWebhookClientCommandHandler"/> now writes to the outbox. These pin the handler's
/// behaviour on a healthy connection, observed green before the durable list is wired and required to
/// stay green after: the publish happens on the exits that leave the client enabled, never on the
/// concurrency exit, and always before the commit that flushes it. An assertion that has to be edited
/// is evidence the behaviour moved.
/// </summary>
public class RotateWebhookClientRaceDisableOutboxCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-22T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static RotateWebhookClientCommand CreateCommand() =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.None);

    private static FakeWebhookIntegrationStatusLookup ActiveThenRevoked()
    {
        FakeWebhookIntegrationStatusLookup status = new();
        status.EnqueueStatuses(WebhookIntegrationStatus.Active, WebhookIntegrationStatus.Revoked);
        return status;
    }

    [Fact]
    public async Task A_failed_save_of_the_race_disable_publishes_the_retry_once_before_the_commit()
    {
        InMemoryRegisteredClientRepository repo = new();
        repo.FailSecondSaveWith = new DbUpdateException("The save of the race disable failed.");
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        SnapshottingTransactionalCommit commit = new(bus);
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, ActiveThenRevoked(), bus, commit, new FakeClock(Now),
            new DisableWebhookClientCommandHandler(
                repo, keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance),
            TimeProvider.System,
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem();
        commit.Snapshots.ShouldHaveSingleItem()
            .OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem(
                "the retry is captured before the single commit that releases it");
    }

    [Fact]
    public async Task A_concurrent_disable_during_the_race_publishes_no_retry()
    {
        InMemoryRegisteredClientRepository repo = new();
        repo.FailSecondSaveWith = new DbUpdateConcurrencyException("The async disable won the race.");
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, ActiveThenRevoked(), bus, new RepositoryBackedTransactionalCommit(repo),
            new FakeClock(Now),
            new DisableWebhookClientCommandHandler(
                repo, keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance),
            TimeProvider.System,
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldBeEmpty(
            "the client is already disabled by the other path, so there is nothing to retry");
    }

    [Fact]
    public async Task A_cancellation_after_Keycloak_disabled_the_client_publishes_the_retry_then_commits()
    {
        var request = new CancellationTokenSource();
        InMemoryRegisteredClientRepository inner = new();
        TokenHonouringRegisteredClientRepository repository = new(inner, request, CancelPoint.Never)
        {
            SaveNeverAnswersOnCall = 2,
        };
        FakeKeycloakAdminClient innerKeycloak = new();
        TokenHonouringKeycloakAdminClient keycloak = new(innerKeycloak);
        FakeEventBus bus = new();
        SnapshottingTransactionalCommit commit = new(bus);
        ManualTimeProvider time = new(Now);
        RotateWebhookClientCommandHandler handler = new(
            repository,
            keycloak,
            new TokenHonouringWebhookIntegrationStatusLookup(ActiveThenRevoked()),
            new TokenHonouringEventBus(bus),
            new TokenHonouringTransactionalCommit(commit),
            new FakeClock(Now),
            new DisableWebhookClientCommandHandler(
                repository, keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance),
            time,
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        var handling = handler.HandleAsync(CreateCommand(), request.Token);
        await Task.WhenAny(handling, time.WaitForPendingTimersAsync(1, TestTimeout));
        time.Advance(TimeSpan.FromSeconds(31));

        var failure = await Record.ExceptionAsync(() => handling.WaitAsync(TestTimeout));

        failure.ShouldBeNull();
        innerKeycloak.Disabled.ShouldContain("webhook-qa");
        bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem();
        commit.Snapshots.ShouldHaveSingleItem()
            .OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem(
                "the retry is published first so the one commit that saves the disabled row also releases it");
        request.Dispose();
    }
}
