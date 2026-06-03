using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.CameraCatalog.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying CameraCatalog EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "CameraCatalog migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {SeededCount} of {TotalCount} simulated dev cameras.")]
    public static partial void SeededSimulatedCameras(this ILogger logger, int seededCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to seed simulated dev camera {CameraName}.")]
    public static partial void SimulatedCameraSeedingFailed(this ILogger logger, Exception exception, string cameraName);
}
