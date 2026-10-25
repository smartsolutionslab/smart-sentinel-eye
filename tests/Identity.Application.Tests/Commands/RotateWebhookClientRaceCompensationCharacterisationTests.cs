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
/// Characterisation for spec 462 (#3138), which moves the post-commit race compensation out of
/// <see cref="RotateWebhookClientCommandHandler"/> into its own class. Two behaviours no existing test
/// pins, observed green on the unmodified handler and required to stay green after the move: a
/// non-cancellation failure of the re-check read is not converted into anything, and an expired bound
/// during the create branch's re-check (where the cancellation filter is false) is a failed disable.
/// An assertion that has to be edited is evidence the behaviour moved.
/// </summary>
public class RotateWebhookClientRaceCompensationCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-22T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static RotateWebhookClientCommand CreateCommand() =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.None);

    private static RotateWebhookClientCommand RotateCommand() =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), Option<int>.Some(3));

    private sealed class Harness
    {
        public Harness(IWebhookIntegrationStatusLookup status, TimeProvider time)
        {
            Handler = new RotateWebhookClientCommandHandler(
                Repository,
                Keycloak,
                status,
                Bus,
                Commit,
                new FakeClock(Now),
                new DisableWebhookClientCommandHandler(
                    Repository, Keycloak, new FakeClock(Now), NullLogger<DisableWebhookClientCommandHandler>.Instance),
                time,
                Logger);
        }

        public InMemoryRegisteredClientRepository Repository { get; } = new();

        public FakeKeycloakAdminClient Keycloak { get; } = new();

        public FakeEventBus Bus { get; } = new();

        public NoOpTransactionalCommit Commit { get; } = new();

        public CapturingLogger<RotateWebhookClientCommandHandler> Logger { get; } = new();

        public RotateWebhookClientCommandHandler Handler { get; }

        public async Task SeedExistingClientAsync()
        {
            var client = RegisteredClient.Register(
                ClientId.From("webhook-qa"),
                ClientKind.WebhookIntegration,
                FabIdentifier.From("munich"),
                OperatorIdentifier.From(Guid.CreateVersion7()),
                new FakeClock(Now));
            Repository.Seed(client, version: 3);
            await Keycloak.CreateClientAsync(
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
    }

    [Fact]
    public async Task A_non_cancellation_failure_of_the_post_commit_status_read_propagates_and_nothing_is_announced()
    {
        HttpRequestException failure = new("EventIngestion is down.");
        Harness harness = new(new ActiveThenFailingStatusLookup(failure), TimeProvider.System);

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => harness.Handler.HandleAsync(CreateCommand(), CancellationToken.None));

        thrown.ShouldBeSameAs(failure);
        harness.Repository.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty();
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldBeEmpty();
        harness.Commit.Commits.ShouldBe(0);
        harness.Keycloak.Disabled.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_non_cancellation_failure_of_the_post_commit_status_read_on_a_rotation_propagates_and_nothing_is_announced()
    {
        HttpRequestException failure = new("EventIngestion is down.");
        Harness harness = new(new ActiveThenFailingStatusLookup(failure), TimeProvider.System);
        await harness.SeedExistingClientAsync();

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => harness.Handler.HandleAsync(RotateCommand(), CancellationToken.None));

        thrown.ShouldBeSameAs(failure);
        harness.Repository.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldBeEmpty();
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldBeEmpty();
        harness.Commit.Commits.ShouldBe(0);
        harness.Keycloak.Disabled.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bound_that_expires_during_the_create_recheck_is_a_failed_disable_and_the_rotation_is_announced()
    {
        ManualTimeProvider time = new(Now);
        Harness harness = new(new ActiveThenNeverAnswersStatusLookup(), time);

        var handling = harness.Handler.HandleAsync(CreateCommand(), CancellationToken.None);
        await Task.WhenAny(handling, time.WaitForPendingTimersAsync(1, TestTimeout));
        time.Advance(TimeSpan.FromSeconds(31));

        var result = await handling.WaitAsync(TestTimeout);

        result.IsSuccess.ShouldBeTrue("the create committed, so an expired re-check still answers success");
        harness.Logger.Named("CouldNotDisableClientCreatedDuringRevokeRace").ShouldHaveSingleItem();
        harness.Logger.Named("RotationRecheckStatusUnverifiable").ShouldBeEmpty();
        harness.Bus.Published.OfType<WebhookClientRaceDisableFailed>().ShouldHaveSingleItem()
            .IntegrationName.ShouldBe("qa");
        harness.Bus.Published.OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        harness.Commit.Commits.ShouldBe(1);
        harness.Repository.Clients.ShouldHaveSingleItem().DisabledAt.ShouldBeNull();
        harness.Keycloak.Disabled.ShouldBeEmpty();
    }

    private sealed class ActiveThenFailingStatusLookup(Exception failure) : IWebhookIntegrationStatusLookup
    {
        private int calls;

        public Task<WebhookIntegrationStatus> GetStatusAsync(
            FabIdentifier fab, string integrationName, CancellationToken cancellationToken) =>
            ++calls > 1
                ? Task.FromException<WebhookIntegrationStatus>(failure)
                : Task.FromResult(WebhookIntegrationStatus.Active);
    }

    private sealed class ActiveThenNeverAnswersStatusLookup : IWebhookIntegrationStatusLookup
    {
        private int calls;

        public async Task<WebhookIntegrationStatus> GetStatusAsync(
            FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
        {
            if (++calls > 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return WebhookIntegrationStatus.Active;
        }
    }
}
