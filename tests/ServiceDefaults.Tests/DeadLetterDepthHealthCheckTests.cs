using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using SmartSentinelEye.ServiceDefaults.Tests.Fakes;
using Wolverine.Runtime;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// ADR-0189 ruling 3, ADR-0154 (issue #3149). The <c>dead-letters</c> readiness check reports a
/// non-empty dead-letter queue as Degraded and never as Unhealthy, and a broker it cannot reach
/// as Healthy.
/// </summary>
public sealed class DeadLetterDepthHealthCheckTests
{
    private const string TypeName = "SmartSentinelEye.ServiceDefaults.DeadLetterDepthHealthCheck";

    private static Type? CheckType => typeof(WolverineDefaults).Assembly.GetType(TypeName);

    [Fact]
    public void The_check_exists_and_is_a_health_check()
    {
        var type = CheckType;

        type.ShouldNotBeNull("ServiceDefaults declares no DeadLetterDepthHealthCheck");
        typeof(IHealthCheck).IsAssignableFrom(type).ShouldBeTrue();
    }

    [Fact]
    public void The_check_watches_the_wolverine_dead_letter_queue_and_treats_one_message_as_concerning()
    {
        var type = CheckType;

        type.ShouldNotBeNull("ServiceDefaults declares no DeadLetterDepthHealthCheck");
        type.GetField("QueueName")?.GetRawConstantValue().ShouldBe("wolverine-dead-letter-queue");
        type.GetField("ConcerningDepth")?.GetRawConstantValue().ShouldBe(1L);
    }

    [Fact]
    public async Task An_unreachable_broker_reads_healthy_with_the_error_in_the_data()
    {
        // An unstarted Wolverine host throws InvalidOperationException, which is not a broker failure,
        // so the seam throws what a down broker does.
        var check = new DeadLetterDepthHealthCheck(
            _ => throw BrokerDown(),
            NullLogger<DeadLetterDepthHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("dead-letters", check, HealthStatus.Degraded, ["ready"]),
            });

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data.ShouldContainKey("error");
    }

    [Fact]
    public async Task An_unstarted_wolverine_runtime_is_not_reported_healthy()
    {
        using var host = UnstartedHost();
        var check = new DeadLetterDepthHealthCheck(
            () => host.Services.GetRequiredService<IWolverineRuntime>(),
            NullLogger<DeadLetterDepthHealthCheck>.Instance);

        // Wolverine's "no connection yet" is an InvalidOperationException; it is the check being
        // unable to read, not the broker being away, so it escapes to the Degraded failure status.
        await Should.ThrowAsync<InvalidOperationException>(() => check.CheckHealthAsync(new HealthCheckContext()));
    }

    [Fact]
    public async Task An_unreachable_broker_is_logged_at_warning()
    {
        var logger = new CapturingLogger<DeadLetterDepthHealthCheck>();
        var check = new DeadLetterDepthHealthCheck(_ => throw BrokerDown(), logger);

        await check.CheckHealthAsync(new HealthCheckContext());

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("AlreadyClosedException", StringComparison.Ordinal));
    }

    [Fact]
    public void A_closed_connection_is_unreachable()
    {
        var closed = new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Library, 320, "CONNECTION_FORCED"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(closed).ShouldBeTrue();
    }

    [Fact]
    public void A_dropped_socket_or_a_timeout_is_unreachable()
    {
        DeadLetterDepthHealthCheck.IsBrokerUnreachable(new SocketException()).ShouldBeTrue();
        DeadLetterDepthHealthCheck.IsBrokerUnreachable(new TimeoutException()).ShouldBeTrue();
    }

    [Fact]
    public void An_interruption_without_a_channel_level_reply_code_is_unreachable()
    {
        var interrupted = new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(interrupted).ShouldBeTrue();
    }

    [Fact]
    public void A_channel_level_refusal_is_the_broker_answering_and_is_not_unreachable()
    {
        var notFound = new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(notFound).ShouldBeFalse();
    }

    [Theory]
    [InlineData(311)]
    [InlineData(312)]
    [InlineData(313)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(406)]
    public void Every_channel_level_reply_code_is_the_broker_answering(ushort replyCode)
    {
        var refused = new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, replyCode, "refused"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(refused).ShouldBeFalse();
    }

    [Theory]
    [InlineData(541)]
    [InlineData(530)]
    public void A_connection_level_reply_code_is_unreachable(ushort replyCode)
    {
        var refused = new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, replyCode, "refused"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(refused).ShouldBeTrue();
    }

    [Fact]
    public void A_closed_channel_with_a_channel_level_reply_code_is_not_unreachable()
    {
        var closed = new AlreadyClosedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "NOT_FOUND"));

        DeadLetterDepthHealthCheck.IsBrokerUnreachable(closed).ShouldBeFalse();
    }

    [Fact]
    public async Task A_read_that_never_completes_is_bounded_by_the_registration_timeout_and_reads_degraded()
    {
        var check = new DeadLetterDepthHealthCheck(
            _ => new TaskCompletionSource<long?>().Task,
            NullLogger<DeadLetterDepthHealthCheck>.Instance);
        var services = new ServiceCollection()
            .AddLogging()
            .AddHealthChecks()
            .Add(new HealthCheckRegistration("dead-letters", check, HealthStatus.Degraded, ["ready"], TimeSpan.FromMilliseconds(100)))
            .Services
            .BuildServiceProvider();

        var report = await services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));

        var entry = report.Entries["dead-letters"];
        entry.Status.ShouldBe(HealthStatus.Degraded);
        entry.Description.ShouldNotBeNull().ShouldContain("timeout");
    }

    [Fact]
    public async Task An_unrelated_invalid_operation_is_not_reported_healthy()
    {
        var check = new DeadLetterDepthHealthCheck(
            _ => throw new InvalidOperationException("a bug in the check"),
            NullLogger<DeadLetterDepthHealthCheck>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => check.CheckHealthAsync(new HealthCheckContext()));
        DeadLetterDepthHealthCheck.IsBrokerUnreachable(new InvalidOperationException()).ShouldBeFalse();
    }

    [Fact]
    public void The_registration_bounds_the_read_and_maps_a_failure_to_degraded()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddDefaultHealthChecks();
        using var host = builder.Build();

        var registration = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(candidate => candidate.Name == "dead-letters");

        registration.Timeout.ShouldBe(DeadLetterDepthHealthCheck.Timeout);
        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task A_host_with_no_wolverine_runtime_reads_healthy()
    {
        var check = new DeadLetterDepthHealthCheck(() => null, NullLogger<DeadLetterDepthHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data.ShouldContainKey("dead-letters");
    }

    [Fact]
    public async Task A_runtime_with_no_rabbitmq_transport_reads_healthy_without_creating_one()
    {
        var options = new Wolverine.WolverineOptions();
        var runtime = new Moq.Mock<IWolverineRuntime>();
        runtime.SetupGet(candidate => candidate.Options).Returns(options);
        var check = new DeadLetterDepthHealthCheck(() => runtime.Object, NullLogger<DeadLetterDepthHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        options.Transports.OfType<Wolverine.RabbitMQ.Internal.RabbitMqTransport>().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_dead_letter_queue_reads_healthy_with_its_depth()
    {
        var logger = new CapturingLogger<DeadLetterDepthHealthCheck>();
        var check = new DeadLetterDepthHealthCheck(_ => Task.FromResult<long?>(0), logger);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["depth"].ShouldBe(0L);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_dead_letter_reads_degraded_with_its_depth_and_one_log_entry()
    {
        var logger = new CapturingLogger<DeadLetterDepthHealthCheck>();
        var check = new DeadLetterDepthHealthCheck(_ => Task.FromResult<long?>(1), logger);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["depth"].ShouldBe(1L);
        result.Data["queue"].ShouldBe(DeadLetterDepthHealthCheck.QueueName);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldContain("1 message(s) waiting");
    }

    // What the read path throws on a down broker: it never opens a connection, it uses the transport's.
    private static AlreadyClosedException BrokerDown() =>
        new(new ShutdownEventArgs(ShutdownInitiator.Library, 541, "End of stream"));

    private static IHost UnstartedHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:probe-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:1",
            });

        builder.AddWolverineForContext<ProbeDbContext>(
            moduleQueuePrefix: "probe",
            outboxSchema: "wolverine_probe",
            postgresConnectionName: "probe-db",
            configureMore: opts => opts.Discovery.DisableConventionalDiscovery());

        return builder.Build();
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);
}
