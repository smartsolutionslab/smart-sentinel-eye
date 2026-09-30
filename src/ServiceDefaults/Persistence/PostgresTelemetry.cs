using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// Spec 282 (#1140). The one call site for
/// <c>Aspire.Npgsql.EntityFrameworkCore.PostgreSQL</c>'s
/// <c>EnrichNpgsqlDbContext&lt;TDbContext&gt;</c>, wired without changing any
/// behaviour a running service already had (ADR-0154, ADR-0088, ADR-0125).
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
/// configuration, so a value set here always wins over configuration —
/// nothing can flip a setting back on from outside this file.
/// </para>
/// </summary>
public static class PostgresTelemetry
{
    /// <summary>
    /// Enriches <typeparamref name="TDbContext"/>'s existing
    /// <c>AddDbContextFactory</c> registration with Aspire's Npgsql
    /// component, with every setting fixed in code:
    ///
    /// <list type="bullet">
    /// <item><b>Health checks: disabled.</b> Aspire's own check
    /// (<c>AddDbContextCheck</c>) reaches Postgres by name, which ADR-0154
    /// clause 2 bars outright — <c>DependencyHealthCheckGuardTests</c> (T012)
    /// guards this, and its counterfactual shows exactly this check failing
    /// when the setting is flipped. This never changes.</item>
    /// <item><b>Retry: disabled.</b> The component's default,
    /// <c>EnableRetryOnFailure</c>, swaps EF's non-retrying
    /// <c>NpgsqlExecutionStrategy</c> for <c>NpgsqlRetryingExecutionStrategy</c>,
    /// which refuses Wolverine's user-initiated EF transactions
    /// (ADR-0088, <c>UseEntityFrameworkCoreTransactions</c>) and changes the
    /// exception shape <c>OutboxBacklogHealthCheck</c> handles (ADR-0154
    /// §Context). This never changes either.</item>
    /// <item><b>Tracing: enabled.</b> A database command now produces a span
    /// under the request's trace instead of leaving a gap
    /// (<c>PostgresTracingRegistrationTests</c>, T020; FR-008). Registering
    /// the tracer provider does not register a health check or change the
    /// execution strategy — those two switches above are independent and
    /// stay disabled.</item>
    /// <item><b>Metrics: enabled.</b> Npgsql's connection-pool metrics reach
    /// the dashboard, which makes an ADR-0125 cap running at its ceiling
    /// visible there instead of only as latency
    /// (<c>PostgresMetricsRegistrationTests</c>, T021; FR-009).</item>
    /// </list>
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
