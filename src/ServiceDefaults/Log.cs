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
        Message = "Published integration event {EventType} through the ambient handler context.")]
    public static partial void PublishingIntegrationEvent(this ILogger logger, string eventType);

    // "Captured", not "published", and the distinction is the whole feature:
    // the message is held against the write's transaction and leaves only when
    // that commits. A line saying "published" here would restate the belief
    // that made this defect invisible for a year.
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Captured integration event {EventType} in the outbox; it is released when the write commits.")]
    public static partial void CapturingIntegrationEvent(this ILogger logger, string eventType);

    // Warning, and in every environment. The health endpoint that carries the
    // same numbers is mapped in Development only, and production is the one
    // place an outbox grows with nobody watching it (spec 021 FR-009).
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox {Schema} is not draining: {Pending} announcement(s) waiting, most-retried has failed {Attempts} time(s).")]
    public static partial void OutboxBacklogConcerning(this ILogger logger, string schema, long pending, int attempts);

    // Names the client, because one message now covers what four per-context
    // ones used to. The category still identifies the caller — the wrapper
    // passes its own ILogger<T> — but a grep for the client id is what an
    // operator actually reaches for when a service account stops working.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Minted a client_credentials token for '{ClientIdentifier}' (expires in {ExpiresIn}s).")]
    public static partial void MintedClientCredentialsToken(
        this ILogger logger, string clientIdentifier, int expiresIn);
}
