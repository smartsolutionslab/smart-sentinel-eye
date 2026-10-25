using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine.Runtime;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// ADR-0189 ruling 3, ADR-0154 (issue #3149). The <c>dead-letters</c> readiness check reports a
/// non-empty dead-letter queue as Degraded and never as Unhealthy, and a broker it cannot reach
/// as Healthy.
///
/// <para>
/// <b>Written against the plan's public surface only.</b> The type does not exist yet and the
/// depth source is a seam the engineer introduces, so the depth 0 and depth 1 outcomes are
/// asserted live in <c>DeadLetterDepthReadinessTests</c> (Integration.Tests) rather than faked
/// here. The type is reached by name through reflection so that this project still compiles and
/// the red is an assertion about the missing check, not a build failure that would hide the
/// other new tests in this project.
/// </para>
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
        var type = CheckType;
        type.ShouldNotBeNull("ServiceDefaults declares no DeadLetterDepthHealthCheck");

        var runtime = UnreachableBrokerRuntime();
        var logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))!;
        var check = (IHealthCheck)Activator.CreateInstance(type, runtime, logger)!;

        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("dead-letters", check, HealthStatus.Degraded, ["ready"]),
            });

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data.ShouldContainKey("error");
    }

    private static IWolverineRuntime UnreachableBrokerRuntime()
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

        var host = builder.Build();
        return host.Services.GetRequiredService<IWolverineRuntime>();
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);
}
