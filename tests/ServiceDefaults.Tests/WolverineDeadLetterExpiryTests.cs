using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Runtime;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// ADR-0189 Decision 1, Postgres half (issue #3149). Every context that composes Wolverine through
/// <c>AddWolverineForContext</c> ages its dead letters out after 14 days; Wolverine's own default is
/// off, so a host that does not say so keeps them forever. Same no-Docker composition as
/// <see cref="DurableOutboxRoutingTests"/>.
/// </summary>
public sealed class WolverineDeadLetterExpiryTests
{
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    private static async Task<IHost> StartAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:probe-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            });

        builder.AddWolverineForContext<ProbeDbContext>(
            moduleQueuePrefix: "probe",
            outboxSchema: "wolverine_probe",
            postgresConnectionName: "probe-db",
            configureMore: opts =>
            {
                opts.Discovery.DisableConventionalDiscovery();
                opts.StubAllExternalTransports();
            });

        builder.Services.DisableAllWolverineMessagePersistence();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task A_composed_host_expires_its_dead_letters()
    {
        using var host = await StartAsync();

        var durability = host.Services.GetRequiredService<IWolverineRuntime>().Options.Durability;

        durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_composed_host_keeps_dead_letters_for_fourteen_days()
    {
        using var host = await StartAsync();

        var durability = host.Services.GetRequiredService<IWolverineRuntime>().Options.Durability;

        durability.DeadLetterQueueExpiration.ShouldBe(TimeSpan.FromDays(14));
    }
}
