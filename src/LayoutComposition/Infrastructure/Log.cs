using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying LayoutComposition EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "LayoutComposition migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for LayoutRevisionPublished({Layout},{Revision}) failed; reconcile-on-reconnect will recover.")]
    public static partial void LayoutRevisionPublishedBroadcastFailed(this ILogger logger, Exception exception, LayoutIdentifier layout, LayoutRevisionNumber revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for LayoutRevisionArchived({Layout},{Revision}) failed; reconcile-on-reconnect will recover.")]
    public static partial void LayoutRevisionArchivedBroadcastFailed(this ILogger logger, Exception exception, LayoutIdentifier layout, LayoutRevisionNumber revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for OverlayRevisionPublished({Overlay},{Revision}) failed; reconcile-on-reconnect will recover.")]
    public static partial void OverlayRevisionPublishedBroadcastFailed(this ILogger logger, Exception exception, Guid overlay, int revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for OverlayRevisionArchived({Overlay},{Revision}) failed; reconcile-on-reconnect will recover.")]
    public static partial void OverlayRevisionArchivedBroadcastFailed(this ILogger logger, Exception exception, Guid overlay, int revision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for ResolvedOverlayTextChanged({Overlay}, v{Version}) failed; reconcile-on-reconnect will recover.")]
    public static partial void ResolvedOverlayTextChangedBroadcastFailed(this ILogger logger, Exception exception, Guid overlay, long version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SignalR broadcast for OverlayHighlightChanged({Overlay}, {DurationMs} ms) failed; reconcile-on-reconnect will recover.")]
    public static partial void OverlayHighlightChangedBroadcastFailed(this ILogger logger, Exception exception, Guid overlay, int durationMs);
}
