using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.CameraCatalog.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying Camera Catalog EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera Catalog migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
