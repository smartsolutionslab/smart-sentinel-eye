using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Forwards PostgreSQL notices — anything a migration emits with
/// <c>RAISE WARNING</c> or <c>RAISE NOTICE</c> — to the MigrationRunner's
/// logger.
///
/// <para>
/// Without this they reach nothing. Npgsql surfaces them on
/// <see cref="NpgsqlConnection.Notice"/>, nobody subscribed, and the message
/// was discarded. Two migrations rely on being heard: the spec 013 and 014
/// backfills each announce how many rows they attributed to a fab, because the
/// assumption behind them — that everything predating the feature belongs to
/// the one fab that was live — cannot be checked from inside the database. The
/// whole point is to put that in front of whoever applies the migration, at
/// the moment it is applied, rather than leave it to be discovered when a
/// fab's screens go blank (#1394).
/// </para>
///
/// <para>
/// Registered in <c>MigrationRunner</c> only, not in <c>ServiceDefaults</c>.
/// Migration time is where a notice is worth interrupting someone for; at
/// request time the same handler would report routine server chatter on every
/// connection in every service.
/// </para>
///
/// <para>
/// Attached through <see cref="IDbContextOptionsConfiguration{TContext}"/>
/// rather than by registering <c>IInterceptor</c> in DI. EF does not discover
/// interceptors from the application service provider here — each context's
/// <c>Add&lt;Context&gt;Persistence</c> builds its own options and calls
/// <c>AddInterceptors</c> itself, and a DI registration is simply not read.
/// That was verified rather than assumed: EF logged
/// <c>initialized 'SystemVariablesDbContext' ... with options: None</c> while
/// a DI-registered interceptor sat unused.
/// </para>
/// </summary>
internal sealed class PostgresNoticeLoggingInterceptor(
    ILogger<PostgresNoticeLoggingInterceptor> logger) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Subscribe(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Subscribe(connection);

        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        Unsubscribe(connection);
        base.ConnectionClosed(connection, eventData);
    }

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        Unsubscribe(connection);

        return base.ConnectionClosedAsync(connection, eventData);
    }

    // Method group rather than a lambda, so the -= below removes the same
    // delegate. Connections are pooled and reopened, and a lambda would
    // subscribe afresh every time while never detaching — one notice would
    // then be logged once per open the connection had ever seen.
    private void Subscribe(DbConnection connection)
    {
        if (connection is NpgsqlConnection npgsql)
        {
            npgsql.Notice -= OnNotice;
            npgsql.Notice += OnNotice;
        }
    }

    private void Unsubscribe(DbConnection connection)
    {
        if (connection is NpgsqlConnection npgsql)
        {
            npgsql.Notice -= OnNotice;
        }
    }

    private void OnNotice(object sender, NpgsqlNoticeEventArgs args)
    {
        PostgresNotice notice = args.Notice;

        // Severity is mapped, not flattened. A migration's RAISE WARNING
        // arriving at Debug would be filtered out by default, which is the
        // same silence this class exists to end.
        if (string.Equals(notice.Severity, "WARNING", StringComparison.OrdinalIgnoreCase))
        {
            logger.PostgresWarning(notice.MessageText, notice.SqlState);

            return;
        }

        logger.PostgresNotice(notice.Severity, notice.MessageText);
    }
}
