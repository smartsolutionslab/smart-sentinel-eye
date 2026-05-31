using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.SystemVariables.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed: overlay-designer returned {Status}; starting with empty index. The index will populate as new OverlayRevisionPublishedV1 events arrive.")]
    public static partial void SeedNonSuccessStatus(ILogger logger, HttpStatusCode status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed: response missing 'published' key; index left empty.")]
    public static partial void SeedMissingPublishedKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "ReverseIndex seeded with {Count} published overlays.")]
    public static partial void SeededOverlays(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed failed; starting with empty index. Self-heal will kick in as overlay V1 events arrive.")]
    public static partial void SeedFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying SystemVariables EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SystemVariables migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
