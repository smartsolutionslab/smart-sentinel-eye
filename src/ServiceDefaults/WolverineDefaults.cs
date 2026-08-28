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
using SmartSentinelEye.ServiceDefaults.Persistence;
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
        int listenerCount = 1,
        bool useNativeAcks = false,
        Action<WolverineOptions> configureMore = null)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleQueuePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionName);
        ArgumentOutOfRangeException.ThrowIfLessThan(listenerCount, 1);

        // The message store is the service's *second* Postgres pool, not a share
        // of the DbContext's — same connection string, separate
        // NpgsqlDataSource. It is half the platform's connection demand and is
        // budgeted as such (ADR-0125).
        string postgresConnection = builder.GetBoundedPostgresConnectionString(postgresConnectionName);

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

                    // Arrays are never integration events here, but they ARE how
                    // Wolverine names a batch: BatchMessagesOf<T> assembles a
                    // T[] and hands it to a handler. Left in, this convention
                    // claims that T[] too and gives it a RabbitMQ queue, so an
                    // assembled batch is published back to the broker and read
                    // again instead of executing locally — a round trip per
                    // batch, added by a convention that was only ever meant to
                    // name listeners (ADR-0127).
                    routing.ExcludeTypes(messageType => messageType.IsArray);

                    // ONE ConfigureListeners call, deliberately. A second call
                    // replaces the first rather than composing with it, so
                    // splitting these into two silently reverted the listener
                    // count to Wolverine's default of 1 — observed as
                    // `"consumers":1` on the queue while the code plainly asked
                    // for four.
                    routing.ConfigureListeners((listener, _) =>
                    {
                        // One listener per queue was Wolverine's default rather
                        // than a decision, and it is a throughput ceiling: a
                        // queue drains at one handler's rate no matter how much
                        // the process has left. Contexts that need more say so;
                        // the rest keep the default (ADR-0124).
                        if (listenerCount > 1)
                        {
                            listener.ListenerCount(listenerCount);
                        }

                        // Settle each delivery at the broker when the handler
                        // finishes, instead of writing it to the Postgres inbox
                        // first (ADR-0126).
                        //
                        // Not a durability trade: the delivery stays
                        // unacknowledged on RabbitMQ for the whole handler, so a
                        // crash mid-flight is redelivered — the broker holds
                        // what the inbox was holding. What is given up is the
                        // inbox's dedup, which only a context whose write is
                        // already idempotent can afford to lose.
                        if (useNativeAcks)
                        {
                            listener.ProcessInParallelWithNativeAcks();
                        }
                    });
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

        // And beside those for a third time: a context that publishes from a
        // background service needs to give what it publishes a cause, and must
        // not own an ActivitySource to do it (spec 026). Named for the
        // application because ConfigureOpenTelemetry already exports that
        // source; a name of its own would be a second thing to keep in step.
        builder.Services.AddSingleton<IJourneyOrigin>(
            _ => new JourneyOrigin(builder.Environment.ApplicationName));

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
