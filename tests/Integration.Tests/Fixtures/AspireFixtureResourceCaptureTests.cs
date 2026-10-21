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
