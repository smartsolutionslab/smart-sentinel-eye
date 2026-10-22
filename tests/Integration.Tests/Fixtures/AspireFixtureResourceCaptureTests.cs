using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// The timeout report is only as good as what the capture records, and a
/// formatter test over hand-built snapshots cannot see a capture line that was
/// deleted. These publish real snapshots through an in-process
/// <see cref="ResourceNotificationService"/>, so they run without Docker.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AspireFixtureResourceCaptureTests : IAsyncLifetime
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_running_resource_with_a_failing_check_has_its_health_captured()
    {
        var notifications = NewNotificationService();
        ContainerResource rabbitmq = new("rabbitmq");
        ContainerResource cameraCatalog = new("camera-catalog");
        await notifications.PublishUpdateAsync(
            rabbitmq,
            // WithHealthReports reads the state to compute the aggregate, so it
            // must come after State is set — Aspire reports no health off Running.
            snapshot => (snapshot with { State = KnownResourceStates.Running })
                .WithHealthReports([new HealthReportSnapshot("rabbitmq_check", HealthStatus.Unhealthy, "Connection refused", null)]));
        await notifications.PublishUpdateAsync(cameraCatalog, snapshot => snapshot with
        {
            State = KnownResourceStates.Waiting,
        });

        var captured = await AspireFixture.CaptureResourceStatesAsync(notifications, Window);

        captured.States["rabbitmq"].ShouldBe("Running");
        captured.Health["rabbitmq"].Status.ShouldBe(HealthStatus.Unhealthy);
        captured.Health["rabbitmq"].Reports.Single().Name.ShouldBe("rabbitmq_check");
    }

    [Fact]
    public async Task A_resource_that_is_not_running_has_no_health_captured()
    {
        var notifications = NewNotificationService();
        ContainerResource cameraCatalog = new("camera-catalog");
        await notifications.PublishUpdateAsync(cameraCatalog, snapshot => snapshot with
        {
            State = KnownResourceStates.Waiting,
        });

        var captured = await AspireFixture.CaptureResourceStatesAsync(notifications, Window);

        captured.States["camera-catalog"].ShouldBe("Waiting");
        captured.Health.ShouldNotContainKey("camera-catalog");
    }

    private const string ProbeSentence = "MediaMTX /v3/paths/list was not reachable after 60 attempts.";

    [Fact]
    public async Task A_probe_that_runs_out_of_attempts_reports_every_resources_state_and_health()
    {
        var notifications = NewNotificationService();
        ContainerResource rabbitmq = new("rabbitmq");
        ContainerResource cameraCatalog = new("camera-catalog");
        await notifications.PublishUpdateAsync(
            rabbitmq,
            snapshot => (snapshot with { State = KnownResourceStates.Running })
                .WithHealthReports([new HealthReportSnapshot("rabbitmq_check", HealthStatus.Unhealthy, "Connection refused", null)]));
        await notifications.PublishUpdateAsync(cameraCatalog, snapshot => snapshot with
        {
            State = KnownResourceStates.Waiting,
        });

        var exception = await AspireFixture.ProbeExhaustedAsync(ProbeSentence, notifications, Window);

        exception.ShouldBeOfType<TimeoutException>();
        exception.Message.ShouldStartWith(ProbeSentence);
        exception.Message.ShouldContain("Resource states:");
        exception.Message.ShouldContain("  camera-catalog: Waiting");
        exception.Message.ShouldContain("  rabbitmq: Running (health Unhealthy)");
        exception.Message.ShouldContain("rabbitmq_check: Unhealthy - Connection refused");
    }

    [Fact]
    public async Task A_probe_timeout_bounds_health_lines_like_the_startup_timeout()
    {
        var notifications = NewNotificationService();
        ContainerResource rabbitmq = new("rabbitmq");
        await notifications.PublishUpdateAsync(
            rabbitmq,
            snapshot => (snapshot with { State = KnownResourceStates.Running })
                .WithHealthReports(
                [
                    new HealthReportSnapshot("check_1", HealthStatus.Unhealthy, "down", null),
                    new HealthReportSnapshot("check_2", HealthStatus.Unhealthy, "down", null),
                    new HealthReportSnapshot("check_3", HealthStatus.Unhealthy, "down", null),
                    new HealthReportSnapshot("check_4", HealthStatus.Unhealthy, "down", null),
                    new HealthReportSnapshot("check_5", HealthStatus.Unhealthy, "down", null),
                ]));

        var exception = await AspireFixture.ProbeExhaustedAsync(ProbeSentence, notifications, Window);

        exception.Message.ShouldContain("check_3: Unhealthy");
        exception.Message.ShouldNotContain("check_4");
        exception.Message.ShouldContain("(+2 more)");
    }

    [Fact]
    public async Task A_probe_timeout_with_no_snapshots_still_reports_and_does_not_throw()
    {
        var notifications = NewNotificationService();

        var exception = await AspireFixture.ProbeExhaustedAsync(ProbeSentence, notifications, TimeSpan.FromMilliseconds(200));

        exception.Message.ShouldBe($"{ProbeSentence}\nResource states:\n  (app not built)");
    }

    [Fact]
    public void Every_probe_timeout_in_the_fixture_goes_through_the_snapshot()
    {
        var fixtures = System.IO.Path.Combine(RepositoryRoot(), "tests", "Integration.Tests", "Fixtures");
        var all = string.Concat(
            System.IO.Directory.GetFiles(fixtures, "AspireFixture*.cs")
                .Where(path => !System.IO.Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.Ordinal))
                .Select(System.IO.File.ReadAllText));

        System.Text.RegularExpressions.Regex.Count(all, @"throw new TimeoutException\(").ShouldBe(1);

        var callSites = System.Text.RegularExpressions.Regex.Count(
            all,
            @"ProbeExhaustedAsync\(\s*\$?""[^""]*after 60 attempts[^""]*""");
        callSites.ShouldBe(3);
    }

    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    private readonly List<IDisposable> disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        return Task.CompletedTask;
    }

    private ResourceNotificationService NewNotificationService()
    {
        ResourceLoggerService loggers = new();
        ResourceNotificationService notifications = new(
            NullLogger<ResourceNotificationService>.Instance,
            new FakeHostApplicationLifetime(),
            new ServiceCollection().BuildServiceProvider(),
            loggers);
        disposables.Add(loggers);
        disposables.Add(notifications);
        return notifications;
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _ = 0;
    }
}
