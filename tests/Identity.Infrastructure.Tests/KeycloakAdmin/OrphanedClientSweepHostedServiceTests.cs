using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 320 (#2181) plan §8.3, facts H1-H3 — mirrors
/// <see cref="KioskPrivilegeSweepBoundTests"/>'s composition (Identity's own
/// <c>AddIdentityInfrastructure</c>, three last-wins overrides) but not its
/// per-pass <c>Bound</c>: <c>OrphanedClientSweepHostedService</c> is a
/// <c>BackgroundService</c> whose <c>StartAsync</c> returns at its own
/// <c>Task.Yield()</c> rather than waiting on a bespoke timeout (plan §5,
/// "There is no per-pass Bound like KioskPrivilegeSweepHostedService's").
///
/// <para>
/// Red today: <see cref="NotImplementedException"/> — <c>ExecuteAsync</c> and
/// <c>RunOnceAsync</c> are T001 declarations. Green once T012 implements them
/// per plan §5.
/// </para>
/// </summary>
public class OrphanedClientSweepHostedServiceTests
{
    [Fact]
    public async Task StartAsync_returns_promptly_while_the_pass_hangs_on_a_keycloak_that_never_answers()
    {
        SilentKeycloakAdminClient keycloak = new();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        CapturingLogger<OrphanedClientSweepHostedService> logger = new();
        using ServiceProvider provider = Compose(keycloak, timeProvider, logger);

        OrphanedClientSweepHostedService service = BuildHostedService(provider);

        using CancellationTokenSource hostStart = new();
        try
        {
            Task startTask = service.StartAsync(hostStart.Token);

            await startTask.WaitAsync(TimeSpan.FromSeconds(2));
            startTask.IsCompletedSuccessfully.ShouldBeTrue(
                "BackgroundService.StartAsync must return without waiting for ExecuteAsync to "
                + "finish — a sweep that has not completed must never hold up Identity's own boot "
                + "(spec 320 plan §5: 'There is no per-pass Bound like "
                + "KioskPrivilegeSweepHostedService's'; StartAsync returns at its own Task.Yield()).");

            bool reachedKeycloak = await WaitUntilAsync(
                () => keycloak.StampedClientAttempts >= 1, TimeSpan.FromSeconds(2));
            reachedKeycloak.ShouldBeTrue(
                "the startup pass must actually have asked Keycloak (S5: it is read first), not "
                + "merely have started and done nothing.");
        }
        finally
        {
            await hostStart.CancelAsync();
            await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task A_throwing_pass_is_logged_and_the_service_keeps_ticking_to_the_next_pass()
    {
        ThrowingStampedClientKeycloakAdminClient keycloak = new();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        CapturingLogger<OrphanedClientSweepHostedService> logger = new();
        using ServiceProvider provider = Compose(keycloak, timeProvider, logger);

        OrphanedClientSweepHostedService service = BuildHostedService(provider);

        using CancellationTokenSource hostStart = new();
        try
        {
            await service.StartAsync(hostStart.Token);

            bool firstPassRan = await WaitUntilAsync(() => keycloak.Attempts >= 1, TimeSpan.FromSeconds(2));
            firstPassRan.ShouldBeTrue("the startup pass must have run at least once");

            bool firstFailureLogged = await WaitUntilAsync(
                () => logger.Named("OrphanedClientSweepFailed").Count >= 1, TimeSpan.FromSeconds(2));
            firstFailureLogged.ShouldBeTrue(
                "a pass that throws must be logged, not silently dropped — a janitor that goes "
                + "quiet looks identical to a healthy one, and the next tick is the only recovery "
                + "(spec 320 plan §5).");

            bool timerRequested = await timeProvider.WaitForPendingTimersAsync(1, TimeSpan.FromSeconds(2));
            timerRequested.ShouldBeTrue(
                "the hosted service must register a PeriodicTimer against this TimeProvider for "
                + "its hourly tick (OrphanedClientSweepHostedService.TickInterval).");

            // Test-owned, not a reference to the production constant — the same
            // choice KioskPrivilegeSweepBoundTests makes for its own Bound, and
            // for the same reason: a later change to the production value must
            // fail this test rather than silently move the assertion with it.
            timeProvider.Advance(TimeSpan.FromHours(1));

            bool secondPassRan = await WaitUntilAsync(() => keycloak.Attempts >= 2, TimeSpan.FromSeconds(2));
            secondPassRan.ShouldBeTrue(
                "one tick's failure must not stop the next tick — Identity keeps serving requests "
                + "and the sweep keeps trying (spec 320, the same discipline as "
                + "KioskPrivilegeSweepHostedService's startup failure).");
        }
        finally
        {
            await hostStart.CancelAsync();
            await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Stopping_cancels_a_running_pass_without_an_unhandled_exception()
    {
        SilentKeycloakAdminClient keycloak = new();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        CapturingLogger<OrphanedClientSweepHostedService> logger = new();
        using ServiceProvider provider = Compose(keycloak, timeProvider, logger);

        OrphanedClientSweepHostedService service = BuildHostedService(provider);

        using CancellationTokenSource hostStart = new();
        await service.StartAsync(hostStart.Token);

        bool reachedKeycloak = await WaitUntilAsync(
            () => keycloak.StampedClientAttempts >= 1, TimeSpan.FromSeconds(2));
        reachedKeycloak.ShouldBeTrue(
            "the pass must actually be running, parked on Keycloak, before shutdown races it");

        await hostStart.CancelAsync();

        Exception? thrown = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        thrown.ShouldBeNull(
            "host shutdown during a running pass must not surface as an unhandled exception from "
            + "StopAsync — the sweep is a background reconciliation nobody is waiting on "
            + "(spec 320 plan §5).");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        return condition();
    }

    private static OrphanedClientSweepHostedService BuildHostedService(IServiceProvider provider) =>
        ActivatorUtilities.CreateInstance<OrphanedClientSweepHostedService>(provider);

    /// <summary>
    /// Identity's own composition — <c>AddIdentityInfrastructure</c> — with
    /// three last-wins overrides: the provider, the <see cref="TimeProvider"/>
    /// the hourly tick is built from, and the hosted service's own logger.
    /// Mirrors <see cref="KioskPrivilegeSweepBoundTests"/>'s <c>Compose</c>.
    /// </summary>
    private static ServiceProvider Compose(
        IKeycloakAdminClient keycloak, TimeProvider timeProvider, ILogger<OrphanedClientSweepHostedService> logger)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080",
            ["ConnectionStrings:identity-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused:unused@127.0.0.1:5672",
        });

        builder.AddIdentityInfrastructure();

        builder.Services.AddScoped(_ => keycloak);
        builder.Services.AddSingleton(timeProvider);
        builder.Services.AddSingleton(logger);

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// Throws from the very first call <c>OrphanedClientSweep.SweepAsync</c>
    /// makes (S5), counting attempts — H2's "a pass that fails" without
    /// pulling in <c>FakeKeycloakAdminClient</c> from
    /// <c>Identity.Application.Tests</c>, which this project deliberately
    /// cannot reference.
    /// </summary>
    private sealed class ThrowingStampedClientKeycloakAdminClient : IKeycloakAdminClient
    {
        public int Attempts { get; private set; }

        public Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("Keycloak refused the listing.");
        }

        public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
            string clientId, CancellationToken cancellationToken) => throw NotPartOfThisTest();

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw NotPartOfThisTest();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) => throw NotPartOfThisTest();

        public Task<KeycloakClientCredentials> ReadClientSecretAsync(
            string clientId, CancellationToken cancellationToken) => throw NotPartOfThisTest();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw NotPartOfThisTest();

        public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) => throw NotPartOfThisTest();

        public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(CancellationToken cancellationToken) =>
            throw NotPartOfThisTest();

        public Task<bool> StripInheritedRealmRolesAsync(string clientId, CancellationToken cancellationToken) =>
            throw NotPartOfThisTest();

        private static NotSupportedException NotPartOfThisTest() =>
            new("OrphanedClientSweepHostedServiceTests only exercises GetStampedClientsAsync.");
    }
}
