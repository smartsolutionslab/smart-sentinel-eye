using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 317 (#2170) — <b>the startup sweep must give up within its own bound,
/// not the resilience pipeline's ~30 s total budget.</b>
///
/// <para>
/// Today <c>KioskPrivilegeSweepHostedService.StartAsync</c> awaits the pass with
/// nothing but the host's own token; against a black-holed Keycloak the only
/// thing that ends the wait is <c>AddStandardResilienceHandler</c>'s 30 s total
/// timeout (measured 30154 ms, spec 092 §5). This file pins the two outcomes
/// the fix owes: the outage is bounded (<see
/// cref="A_keycloak_that_never_answers_is_abandoned_at_the_bound_and_the_boot_continues"/>,
/// red today) and host shutdown is never mistaken for hitting that bound (<see
/// cref="Host_shutdown_during_the_sweep_is_not_mistaken_for_the_bound"/>, already
/// green — a characterisation guard for FR-004).
/// </para>
///
/// <para>
/// <b>The bound's value is test-owned</b> (spec 317 plan §3): a local
/// <c>TimeSpan</c>, not a reference to the production constant, which does not
/// exist yet. That keeps this file compiling against today's two-parameter
/// constructor — the red below is a runtime failure that can be quoted, not a
/// compile error — and means a later change to the production value fails this
/// test rather than silently moving the assertion with it.
/// </para>
///
/// <para>
/// Composed exactly as <see cref="KioskPrivilegeSweepStartupTests"/>'s
/// <c>Compose</c> — <c>AddIdentityInfrastructure</c>, then last-wins overrides —
/// with two more overrides this spec needs: the <see cref="TimeProvider"/> the
/// bound is built from, and the hosted service's own
/// <see cref="ILogger{TCategoryName}"/>, so the new Warning is visible to an
/// assertion (the pre-existing tests pass <c>NullLogger</c>, which is why spec
/// 092's own logging change shipped unseen).
/// </para>
/// </summary>
public class KioskPrivilegeSweepBoundTests
{
    [Fact]
    public async Task A_keycloak_that_never_answers_is_abandoned_at_the_bound_and_the_boot_continues()
    {
        // Spec 317 §4's value, owned by the test rather than read from
        // production: the production constant does not exist on this tree yet.
        TimeSpan bound = TimeSpan.FromSeconds(5);

        SilentKeycloakAdminClient keycloak = new();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        CapturingLogger<KioskPrivilegeSweepHostedService> logger = new();
        using ServiceProvider provider = Compose(keycloak, timeProvider, logger);

        KioskPrivilegeSweepHostedService service = BuildHostedService(provider);

        using CancellationTokenSource hostStart = new();
        Task startTask = service.StartAsync(hostStart.Token);

        try
        {
            // Event-driven, not a fixed sleep: a real-time cap only to stop a
            // regression hanging the suite. Today's StartAsync never asks this
            // provider for a timer at all, so this is where the red happens.
            bool timerRequested = await timeProvider.WaitForPendingTimersAsync(1, TimeSpan.FromSeconds(2));
            timerRequested.ShouldBeTrue(
                "today's StartAsync never asks the TimeProvider for a bound timer, so a "
                + "black-holed Keycloak holds the host's start for however long the resilience "
                + "pipeline's own total budget is (measured 30154 ms, spec 092 §5) instead of a "
                + "fixed few seconds of its own (spec 317 FR-001/FR-006).");

            timeProvider.Advance(bound - TimeSpan.FromTicks(1));
            startTask.IsCompleted.ShouldBeFalse(
                "one tick short of the bound, the sweep must still be waiting — abandoning early "
                + "would cut a healthy pass short (spec 317 §4 point 2).");

            timeProvider.Advance(TimeSpan.FromTicks(1));

            // Real-time guard: a regression here fails the test instead of
            // hanging the suite.
            await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(10)));
            startTask.IsCompleted.ShouldBeTrue(
                "the bound has fully elapsed; StartAsync must have returned (FR-001).");

            await startTask; // Rethrows if StartAsync faulted — FR-002 says it must not.

            keycloak.EnumerationAttempts.ShouldBe(
                1,
                "the pass must actually have asked the provider once before giving up.");

            IReadOnlyList<LoggedEntry> timedOutEntries = logger.Named("KioskPrivilegeSweepTimedOut");
            timedOutEntries.Count.ShouldBe(
                1,
                "abandoning at the bound must log exactly one distinct Warning naming it "
                + "(FR-003).");

            LoggedEntry timedOut = timedOutEntries[0];

            timedOut.Field("Bound").ShouldBe(
                bound.ToString(),
                "the bound is a structured field, not only message text — the same discipline "
                + "spec 122 (#2166) required of the sibling log line.");

            logger.Named("KioskPrivilegeSweepFailed").ShouldBeEmpty(
                "hitting the bound is distinct from the generic failure Warning; logging both "
                + "for the same pass would tell an operator two different, contradictory stories.");
        }
        finally
        {
            await hostStart.CancelAsync();

            try
            {
                await startTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Cleanup only; the assertions above already captured the
                // behaviour under test.
            }
        }
    }

    /// <summary>
    /// Characterisation guard for FR-004, already true on today's code: the
    /// existing <c>catch (Exception exception) when (exception is not
    /// OperationCanceledException)</c> excludes the host's own cancellation, so
    /// shutting the host down during the sweep must still propagate, and must
    /// never be mistaken for — logged the same way as — hitting the bound.
    ///
    /// <para>
    /// Run on the unchanged tree and observed green before any implementation
    /// change (ADR-0139/ADR-0144 §Testing): this is the safety net the fix must
    /// not narrow. An edit to this assertion after the fix lands is evidence the
    /// behaviour moved, not a passing refactor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Host_shutdown_during_the_sweep_is_not_mistaken_for_the_bound()
    {
        SilentKeycloakAdminClient keycloak = new();
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        CapturingLogger<KioskPrivilegeSweepHostedService> logger = new();
        using ServiceProvider provider = Compose(keycloak, timeProvider, logger);

        KioskPrivilegeSweepHostedService service = BuildHostedService(provider);

        using CancellationTokenSource hostStart = new();
        Task startTask = service.StartAsync(hostStart.Token);

        // The sweep is already parked on the black hole by the time this
        // line runs — GetEnrolledKioskClientIdsAsync increments its counter
        // before its first (and only) await.
        await hostStart.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => startTask);

        keycloak.EnumerationAttempts.ShouldBe(
            1, "the host token must have reached the provider's own call, not merely the "
            + "hosted service's entry point, or this would pass against a sweep that cancels "
            + "before ever asking Keycloak.");

        logger.Named("KioskPrivilegeSweepTimedOut").ShouldBeEmpty(
            "the host's own shutdown is not the sweep giving up at its bound — FR-004 forbids "
            + "logging it as one.");

        logger.Named("KioskPrivilegeSweepFailed").ShouldBeEmpty(
            "host shutdown propagates past both catches (StartAsync's own filter already "
            + "excludes OperationCanceledException); it must not be reported as the generic "
            + "failure either.");
    }

    private static KioskPrivilegeSweepHostedService BuildHostedService(IServiceProvider provider) =>
        ActivatorUtilities.CreateInstance<KioskPrivilegeSweepHostedService>(provider);

    /// <summary>
    /// Identity's own composition — <c>AddIdentityInfrastructure</c> — with
    /// three last-wins overrides: the provider, the clock the bound is built
    /// from, and the hosted service's own logger. Constructing
    /// <see cref="KioskPrivilegeSweepHostedService"/> directly would test the
    /// class and not the wiring it is actually started through.
    /// </summary>
    private static ServiceProvider Compose(
        IKeycloakAdminClient keycloak, TimeProvider timeProvider, ILogger<KioskPrivilegeSweepHostedService> logger)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Read while registering, and never connected to: the host is not
        // started, so Wolverine's broker and the DbContext's server are only
        // ever parsed.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080",
            ["ConnectionStrings:identity-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused:unused@127.0.0.1:5672",
        });

        builder.AddIdentityInfrastructure();

        // Last registration wins for each of these three.
        builder.Services.AddScoped(_ => keycloak);
        builder.Services.AddSingleton(timeProvider);
        builder.Services.AddSingleton(logger);

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
