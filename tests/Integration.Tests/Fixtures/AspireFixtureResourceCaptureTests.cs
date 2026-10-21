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
public class AspireFixtureResourceCaptureTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task A_running_resource_with_a_failing_check_has_its_health_captured()
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

    private static ResourceNotificationService NewNotificationService() =>
        new(
            NullLogger<ResourceNotificationService>.Instance,
            new FakeHostApplicationLifetime(),
            new ServiceCollection().BuildServiceProvider(),
            new ResourceLoggerService());

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _ = 0;
    }
}
