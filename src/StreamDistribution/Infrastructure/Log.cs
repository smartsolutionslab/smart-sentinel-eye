using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "MediaMtxReconciler startup pass failed; continuing without reconcile.")]
    public static partial void ReconcilerStartupPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciler removed orphan MediaMTX path {Path}.")]
    public static partial void ReconcilerRemovedOrphanPath(ILogger logger, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciler failed to remove orphan path {Path}; will retry on next restart.")]
    public static partial void ReconcilerFailedToRemoveOrphanPath(ILogger logger, Exception exception, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Information, Message = "MediaMtxReconciler startup pass complete. Configured={Configured}, expected={Expected}, removed={Removed}.")]
    public static partial void ReconcilerStartupPassComplete(ILogger logger, int configured, int expected, int removed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered MediaMTX path {Path} -> {Source}.")]
    public static partial void RegisteredMediaMtxPath(ILogger logger, MediaMtxPath path, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed MediaMTX path {Path}.")]
    public static partial void RemovedMediaMtxPath(ILogger logger, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying Stream Distribution EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream Distribution migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "StreamHealthWatcher started (poll every {Interval}).")]
    public static partial void StreamHealthWatcherStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "StreamHealthWatcher poll iteration failed; will retry next tick.")]
    public static partial void StreamHealthWatcherPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MediaMTX health probe failed for path {Path}; skipping this tick.")]
    public static partial void HealthProbeFailed(ILogger logger, Exception exception, MediaMtxPath path);
}
