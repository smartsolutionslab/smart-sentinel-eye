using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Source-generated log methods for ServiceDefaults (ADR-0050).
/// <c>[LoggerMessage]</c> short-circuits when the level is disabled and
/// avoids per-call template parsing — this rides the integration-event
/// publish path (<see cref="OutboxEventBus{TDbContext}"/>), which fires for every
/// outbound event. It logs at Debug: at the sustained event rate an
/// Information-level entry per publish would be pure noise in production,
/// where the level filter keeps it (and its formatting cost) off.
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Publishing integration event {EventType} via Wolverine.")]
    public static partial void PublishingIntegrationEvent(this ILogger logger, string eventType);

    // "Captured", not "published", and the distinction is the whole feature:
    // the message is held against the write's transaction and leaves only when
    // that commits. A line saying "published" here would restate the belief
    // that made this defect invisible for a year.
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Captured integration event {EventType} in the outbox; it is released when the write commits.")]
    public static partial void CapturingIntegrationEvent(this ILogger logger, string eventType);
}
