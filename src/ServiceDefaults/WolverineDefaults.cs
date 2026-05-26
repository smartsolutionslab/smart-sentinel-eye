using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Applies the Wolverine defaults from ADR-0088 to a host builder:
/// per-module queue isolation (so two contexts subscribed to the same
/// integration event do not become competing consumers), eager transaction
/// mode (paired with the Postgres outbox), Postgres-backed message store,
/// and RabbitMQ transport with conventional routing.
/// </summary>
public static class WolverineDefaults
{
    public static IHostApplicationBuilder AddWolverineForContext<TDbContext>(
        this IHostApplicationBuilder builder,
        string moduleQueuePrefix,
        string outboxSchema,
        string postgresConnectionName,
        string rabbitConnectionName = "rabbitmq",
        Action<WolverineOptions> configureMore = null)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleQueuePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionName);

        string postgresConnection =
            builder.Configuration.GetConnectionString(postgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{postgresConnectionName}' is required for the Wolverine outbox.");

        string rabbitConnection =
            builder.Configuration.GetConnectionString(rabbitConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{rabbitConnectionName}' is required for Wolverine RabbitMQ transport.");

        builder.UseWolverine(opts =>
        {
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

            opts.PersistMessagesWithPostgresql(postgresConnection, outboxSchema);

            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.AutoApplyTransactions();

            opts.UseRabbitMq(new Uri(rabbitConnection))
                .AutoProvision()
                .UseConventionalRouting(routing =>
                {
                    routing.QueueNameForListener(eventType =>
                        $"{moduleQueuePrefix}.{eventType.FullName}");
                });

            configureMore?.Invoke(opts);
        });

        return builder;
    }
}
