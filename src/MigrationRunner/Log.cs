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
    public static partial void RunningMigrations(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "All migrations applied; MigrationRunner exiting.")]
    public static partial void AllMigrationsApplied(ILogger logger);
}
