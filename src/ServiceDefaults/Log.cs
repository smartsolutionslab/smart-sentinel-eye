using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Source-generated log methods for ServiceDefaults (ADR-0050).
/// <c>[LoggerMessage]</c> short-circuits when the level is disabled and
/// avoids per-call template parsing — this rides the integration-event
/// publish path (<see cref="WolverineEventBus"/>), which fires for every
/// outbound event. It logs at Debug: at the sustained event rate an
/// Information-level entry per publish would be pure noise in production,
/// where the level filter keeps it (and its formatting cost) off.
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Publishing integration event {EventType} via Wolverine.")]
    public static partial void PublishingIntegrationEvent(ILogger logger, string eventType);
}
