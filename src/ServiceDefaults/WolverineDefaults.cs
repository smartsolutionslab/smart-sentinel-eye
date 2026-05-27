using System.Reflection;
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
///
/// <para>
/// Convention: Wolverine's handler discovery defaults to the entry
/// assembly (typically the per-context <c>*.Api</c> project). Every
/// context's domain-event handlers live in <c>*.Application</c>, so
/// this method derives that assembly name from the
/// <typeparamref name="TDbContext"/> assembly (replacing the
/// <c>.Infrastructure</c> suffix with <c>.Application</c>) and
/// includes it in the discovery scan. Adding a new bounded context no
/// longer requires hand-rolling <c>IncludeAssembly</c> in its
/// Infrastructure module.
/// </para>
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

        Assembly applicationAssembly = TryLoadApplicationAssembly(typeof(TDbContext).Assembly);

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

            if (applicationAssembly != null)
            {
                opts.Discovery.IncludeAssembly(applicationAssembly);
            }

            configureMore?.Invoke(opts);
        });

        return builder;
    }

    /// <summary>
    /// Locates the <c>*.Application</c> assembly that pairs with the
    /// caller's Infrastructure project by string-rewriting the suffix.
    /// Returns <c>null</c> if no matching assembly is loadable — keeps
    /// the convention silent when a future context legitimately has no
    /// Application handlers to discover.
    /// </summary>
    private static Assembly TryLoadApplicationAssembly(Assembly infrastructureAssembly)
    {
        const string InfrastructureSuffix = ".Infrastructure";
        string name = infrastructureAssembly.GetName().Name ?? string.Empty;
        if (!name.EndsWith(InfrastructureSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        string applicationName = string.Concat(
            name.AsSpan(0, name.Length - InfrastructureSuffix.Length),
            ".Application");

        try
        {
            return Assembly.Load(applicationName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
