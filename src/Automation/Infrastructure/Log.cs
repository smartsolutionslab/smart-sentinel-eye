using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Automation.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded rule cache with {Count} Active rule(s).")]
    public static partial void SeededRuleCache(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rule cache seeding failed; cache will start empty and self-heal on the next Publish.")]
    public static partial void RuleCacheSeedingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying Automation EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automation migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
