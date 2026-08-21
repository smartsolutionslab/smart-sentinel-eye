using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using SmartSentinelEye.Shared.CQRS;

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
            ?? throw new InvalidOperationException($"Connection string '{postgresConnectionName}' is required for the Wolverine outbox.");

        string rabbitConnection =
            builder.Configuration.GetConnectionString(rabbitConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{rabbitConnectionName}' is required for Wolverine RabbitMQ transport.");

        Assembly applicationAssembly = TryLoadApplicationAssembly(typeof(TDbContext).Assembly);

        builder.UseWolverine(opts =>
        {
            // Deliberate, not a leftover. Wolverine 6 defaults this to NotAllowed,
            // which sounds stricter but fails quietly: when codegen cannot build a
            // dependency it drops that handler and the service still starts and
            // reports healthy. Measured, not assumed — removing a single opt-in
            // left the build clean, /health green and the e2e suite passing while
            // cameras stopped provisioning streams entirely.
            //
            // AllowedButWarn falls back to service location and warns instead, so
            // a dependency nobody anticipated still works. Two already need it:
            // IRtspGateway (typed HttpClient behind an opaque lambda factory) and
            // DomainEventDispatcher (takes IServiceProvider, resolving handlers
            // reflectively). Tightening this trades a warning for a silent outage.
            opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

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

        // Spec 021. Every context that gets Wolverine gets the outbox-backed
        // publisher and commit with it, bound to the DbContext this call already
        // names.
        //
        // Registered here rather than once per Infrastructure module — nine
        // identical lines in nine files is nine chances to add a tenth context
        // and forget, and forgetting is silent: the write succeeds, the caller
        // is told the truth, and the announcement is never made.
        builder.Services.AddScoped<IEventBus, OutboxEventBus<TDbContext>>();
        builder.Services.AddScoped<ITransactionalCommit, OutboxTransactionalCommit<TDbContext>>();

        // Registered beside them for the same reason: every context that applies
        // an effect needs to report the leg, and none of them may reference the
        // meter directly (spec 025).
        builder.Services.AddSingleton<ILatencyBudget, EventToOverlayLatency>();

        // FR-008. Trading a silent loss for a silent backlog would not be much
        // of a trade, so the queue depth is visible from the moment the outbox
        // starts being used.
        builder.Services.AddHealthChecks().AddTypeActivatedCheck<OutboxBacklogHealthCheck<TDbContext>>(
            $"outbox-{moduleQueuePrefix}",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready"],
            args: [outboxSchema]);

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

        string applicationName = string.Concat(name.AsSpan(0, name.Length - InfrastructureSuffix.Length), ".Application");

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
