using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Source-generated log methods for the MigrationRunner host (ADR-0050).
/// One-time startup logs, but kept on the same `[LoggerMessage]` pattern
/// as the rest of the solution for consistency.
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Running migrations for {Context}.")]
    public static partial void RunningMigrations(this ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "All migrations applied; MigrationRunner exiting.")]
    public static partial void AllMigrationsApplied(this ILogger logger);

    // A migration said something it wanted heard — the fab backfills raise one
    // naming how many rows they attributed. Warning, not Debug: at Debug the
    // default filter hides it and the message may as well not exist (#1394).
    [LoggerMessage(Level = LogLevel.Warning, Message = "PostgreSQL: {Message} (SQLSTATE {SqlState})")]
    public static partial void PostgresWarning(this ILogger logger, string message, string sqlState);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PostgreSQL {Severity}: {Message}")]
    public static partial void PostgresNotice(this ILogger logger, string severity, string message);
}
