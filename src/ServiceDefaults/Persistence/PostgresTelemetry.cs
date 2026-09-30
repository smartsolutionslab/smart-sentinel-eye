using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// The one call site for
/// <c>Aspire.Npgsql.EntityFrameworkCore.PostgreSQL</c>'s
/// <c>EnrichNpgsqlDbContext&lt;TDbContext&gt;</c> (ADR-0154, ADR-0088,
/// ADR-0125). Health checks, retry and connection-string resolution are
/// wired without changing any behaviour a running service already had;
/// tracing and metrics are new behaviour this helper adds (see the method
/// doc below).
///
/// <para>
/// Every context calls this from its own <c>Add{Context}Infrastructure</c>,
/// immediately after <c>Add{Context}Persistence()</c> registers the
/// <c>DbContext</c> via <c>AddDbContextFactory</c> — never from
/// <c>AddXPersistence</c> itself, so <c>MigrationRunner</c> (ADR-0067), which
/// composes only the persistence path, gains no health check, tracer or
/// meter provider from this. It is also not called from
/// <c>AddWolverineForContext</c>: that would hide a persistence concern
/// inside messaging wiring.
/// </para>
///
/// <para>
/// The delegate below runs after Aspire binds
/// <c>Aspire:Npgsql:EntityFrameworkCore:PostgreSQL[:{TContext}]</c> from
/// configuration, so a value set here always wins over configuration for
/// the four switches set below — nothing external can flip one of them back
/// on through config. That guarantee is scoped to this call site: nothing
/// stops a second, direct <c>EnrichNpgsqlDbContext</c> call elsewhere from
/// setting them differently for the same context.
/// </para>
/// </summary>
public static class PostgresTelemetry
{
    /// <summary>
    /// Enriches <typeparamref name="TDbContext"/>'s existing
    /// <c>AddDbContextFactory</c> registration with Aspire's Npgsql
    /// component. Four settings are fixed in code below and never bound
    /// from configuration for this call site:
    ///
    /// <list type="bullet">
    /// <item><b>Health checks: disabled.</b> Aspire's own check
    /// (<c>AddDbContextCheck</c>) reaches Postgres by name, which ADR-0154
    /// clause 2 bars outright — <c>DependencyHealthCheckGuardTests</c>
    /// guards this. Its counterfactual (flipping this setting and observing
    /// the guard turn red) is recorded in
    /// <c>specs/282-the-reference-nobody-calls/T031-counterfactual.md</c>.
    /// This never changes.</item>
    /// <item><b>Retry: disabled.</b> The component's default,
    /// <c>EnableRetryOnFailure</c>, swaps EF's non-retrying
    /// <c>NpgsqlExecutionStrategy</c> for <c>NpgsqlRetryingExecutionStrategy</c>,
    /// which refuses Wolverine's user-initiated EF transactions
    /// (ADR-0088, <c>UseEntityFrameworkCoreTransactions</c>) and changes the
    /// exception shape <c>OutboxBacklogHealthCheck</c> handles (ADR-0154
    /// §Context). This never changes either.</item>
    /// <item><b>Tracing: enabled.</b> A database command now produces a span
    /// under the request's trace instead of leaving a gap
    /// (<c>PostgresTracingRegistrationTests</c>). Registering the tracer
    /// provider does not register a health check or change the execution
    /// strategy — those two switches above are independent and stay
    /// disabled.</item>
    /// <item><b>Metrics: enabled.</b> Npgsql's connection-pool metrics reach
    /// the dashboard, which makes an ADR-0125 cap running at its ceiling
    /// visible there instead of only as latency
    /// (<c>PostgresMetricsRegistrationTests</c>).</item>
    /// </list>
    ///
    /// <para>
    /// <b>What is not fixed here.</b> <c>CommandTimeout</c> is deliberately
    /// left unset above, so it still binds from
    /// <c>Aspire:Npgsql:EntityFrameworkCore:PostgreSQL[:{TContext}]:CommandTimeout</c>
    /// if that key is ever set. That is not inert: the component's internal
    /// retry-configuration path runs whenever <c>CommandTimeout</c> is set,
    /// independent of <c>DisableRetry</c>. Nothing in this repository sets
    /// that key today, and no test pins that it stays unset.
    /// </para>
    ///
    /// <para>
    /// Connection-string resolution is untouched by either switch:
    /// <c>GetBoundedPostgresConnectionString</c> remains the only source
    /// (ADR-0125), because <c>EnrichNpgsqlDbContext</c> enriches the existing
    /// registration rather than reading <c>ConnectionStrings:{name}</c>
    /// itself.
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder EnrichPostgresDbContext<TDbContext>(this IHostApplicationBuilder builder)
        where TDbContext : DbContext
    {
        Ensure.That(builder).IsNotNull();

        builder.EnrichNpgsqlDbContext<TDbContext>(settings =>
        {
            settings.DisableHealthChecks = true;
            settings.DisableRetry = true;
            settings.DisableTracing = false;
            settings.DisableMetrics = false;
        });

        return builder;
    }
}
