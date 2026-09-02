using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// The one place the platform's Postgres connection arithmetic is written down
/// (ADR-0125, issue 1962).
///
/// <para>
/// Every context runs its own Npgsql pools against one shared Postgres, and
/// Npgsql's default cap is <b>100 connections per pool</b>. Nine contexts with
/// two pools each is a potential demand of 1 800 against a server that allows a
/// few hundred, so the only reason it worked was that the pools never all grew
/// at once. Measured on a run-mode stack, they had grown far enough:
/// <b>97 of 100 connections held before any load</b>.
/// </para>
///
/// <para>
/// <b>The failure that produces is why this is worth a type.</b> When the budget
/// runs out, the service that fails is whichever one asks for a connection next
/// — not the one that consumed it. Driving audit ingest at 100 ev/s took the
/// cluster over and <c>system-variables</c> started refusing writes with
/// <c>53300: sorry, too many clients already</c>, with a stack trace pointing at
/// its own <c>DbContext</c> and nothing anywhere naming the real cause.
/// </para>
/// </summary>
public static class PostgresConnectionBudget
{
    /// <summary>
    /// Cap per pool. A cap is not a reservation — a pool only grows under
    /// demand, so this costs nothing until it is needed.
    ///
    /// <para>
    /// Sized against observation rather than taste: the heaviest consumer is
    /// AuditObservability, whose four listeners (ADR-0124) plus HTTP traffic
    /// peaked at 22 connections across both its pools at 100 ev/s — about 11 per
    /// pool. Twenty leaves that roughly 80% of headroom while keeping the
    /// platform total well inside <see cref="ServerMaxConnections"/>.
    /// </para>
    /// </summary>
    public const int MaxPoolSize = 20;

    /// <summary>
    /// Pools a long-running service opens against its own database: one for the
    /// EF <c>DbContext</c> and one for Wolverine's message store. They carry the
    /// same connection string but are separate <c>NpgsqlDataSource</c>s, so they
    /// do <b>not</b> share a pool — which is exactly why the potential demand is
    /// double what the number of contexts suggests.
    ///
    /// <para>
    /// Counted rather than read off the two call sites: with the cap temporarily
    /// set to 3, every database plateaued at exactly 6 pooled connections under
    /// load.
    /// </para>
    /// </summary>
    public const int PoolsPerService = 2;

    /// <summary>
    /// Connections each database holds that belong to no pool, so no cap
    /// restrains them — found by the same experiment, which showed 8 rather than
    /// the expected 6:
    ///
    /// <list type="bullet">
    /// <item><c>wolverine-advisory-lock:WolverineEnvelopeStorage</c> — a
    /// dedicated connection Wolverine keeps open for its advisory lock.</item>
    /// <item><c>TimescaleDB Background Worker Scheduler</c> — server-side, one
    /// per database carrying the extension. Not a client connection, but it
    /// occupies a <c>max_connections</c> slot just the same.</item>
    /// </list>
    ///
    /// <para>
    /// Eighteen slots across the platform: small, and precisely the kind of
    /// thing a budget derived from reading the code would have missed.
    /// </para>
    /// </summary>
    public const int FixedConnectionsPerDatabase = 2;

    /// <summary>
    /// Long-running services holding a Postgres pool: camera-catalog,
    /// stream-distribution, layout-composition, overlay-designer,
    /// system-variables, event-ingestion, automation, identity,
    /// audit-observability.
    ///
    /// <para>
    /// <c>MigrationRunner</c> is deliberately excluded although it registers all
    /// nine persistence modules: it migrates sequentially and exits, so at most
    /// one of its pools is ever active, and it is finished before the services
    /// take load. <c>ServerMaxConnections</c>' headroom covers it.
    /// </para>
    /// </summary>
    public const int Services = 9;

    /// <summary>
    /// Slots kept back for everything that is not a long-running service: the
    /// <c>MigrationRunner</c>'s nine registrations, pgAdmin in dev, an
    /// operator's <c>psql</c>, and Postgres' own
    /// <c>superuser_reserved_connections</c>.
    ///
    /// <para>
    /// A named number rather than a fraction of the server, because what it has
    /// to cover is a list rather than a proportion — and because the moment it
    /// matters most is a full-saturation incident, where the person diagnosing
    /// it must still be able to connect.
    /// </para>
    /// </summary>
    public const int ReservedForToolingAndOperators = 100;

    /// <summary>
    /// What <c>AppHost</c> starts Postgres with. It is written out there too — an
    /// Aspire project reference exposes no assembly to the AppHost — and the two
    /// are held together by <c>PostgresConnectionBudgetIntegrationTests</c>,
    /// which asks the running server what it allows.
    /// </summary>
    public const int ServerMaxConnections = 500;

    /// <summary>
    /// Worst case if every long-running service saturated every pool at once,
    /// including the connections no pool governs. Asserted against
    /// <see cref="ServerMaxConnections"/> by <c>PostgresConnectionBudgetTests</c>,
    /// so adding a tenth context fails a test instead of a production write.
    /// </summary>
    public static int ServiceCeiling =>
        ((MaxPoolSize * PoolsPerService) + FixedConnectionsPerDatabase) * Services;

    /// <summary>
    /// Reads a Postgres connection string from configuration and applies the
    /// pool cap, throwing if it is not configured.
    ///
    /// <para>
    /// Every persistence module and <c>AddWolverineForContext</c> goes through
    /// this rather than reading configuration directly, because a pool that
    /// escapes the budget does not announce itself — it just moves the failure
    /// to a different service.
    /// </para>
    /// </summary>
    public static string GetBoundedPostgresConnectionString(
        this IHostApplicationBuilder builder, string connectionName)
    {
        Ensure.That(builder).IsNotNull();
        Ensure.That(connectionName).IsNotNull().IsNotNullOrWhiteSpace();

        string connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionName}' is required.");

        return Bounded(connectionString);
    }

    /// <summary>
    /// Applies <see cref="MaxPoolSize"/> unless the connection string already
    /// names one. Honouring an explicit value is the escape hatch — a deployment
    /// that needs a different cap says so in its connection string rather than
    /// needing a new knob here.
    /// </summary>
    public static string Bounded(string connectionString)
    {
        Ensure.That(connectionString).IsNotNull().IsNotNullOrWhiteSpace();

        // Asked of a plain DbConnectionStringBuilder, which holds only the keys
        // the string actually names. NpgsqlConnectionStringBuilder answers
        // ContainsKey for every keyword it knows, defaults included, so it
        // cannot tell "set to 100" from "not set" — and using it here silently
        // left every connection string at Npgsql's default of 100.
        System.Data.Common.DbConnectionStringBuilder written = new() { ConnectionString = connectionString };
        if (written.ContainsKey("Maximum Pool Size") || written.ContainsKey("MaxPoolSize"))
        {
            return connectionString;
        }

        NpgsqlConnectionStringBuilder builder = new(connectionString) { MaxPoolSize = MaxPoolSize };
        return builder.ConnectionString;
    }
}
