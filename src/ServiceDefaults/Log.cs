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

    // #2290 US3. Information, not Debug: a swept reservation means an attempt
    // died somewhere between reserve and complete, and that is worth a line an
    // operator can grep for — unlike the outbox-publish lines above, which fire
    // on every ordinary success. Called only when the count is non-zero, so the
    // expected case (nothing to sweep) logs nothing across seven hourly workers.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Swept {Count} stale idempotency reservation(s) older than the reclamation bound.")]
    public static partial void SweptStaleIdempotencyReservations(this ILogger logger, int count);

    // #2290 phase-6 review. Warning, not Error: a failed sweep costs one
    // skipped hour, not an incident — but it must be visible, since nothing
    // else observes this worker and a silently-failing sweep degrades back to
    // pre-#2290 behaviour with no signal.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The idempotency reservation sweep failed; it will retry on the next tick.")]
    public static partial void IdempotencyReservationSweepFailed(this ILogger logger, Exception exception);

    // Spec 270 (ADR-0160). Transition-only, mirroring WhepAuthValidator's
    // realmUnreachable Interlocked.Exchange pattern: one Warning per outage,
    // not one per failed 5 s refresh attempt.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The revoked-client snapshot could not be refreshed; the previous snapshot is kept (fail-static, ADR-0160 §3).")]
    public static partial void RevokedClientSnapshotUnavailable(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The revoked-client snapshot was restored after a refresh failure.")]
    public static partial void RevokedClientSnapshotRestored(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Loaded the first revoked-client snapshot: {Count} disabled client(s).")]
    public static partial void FirstRevokedClientSnapshotLoaded(this ILogger logger, int count);

    // Logged from the bearer OnTokenValidated hook and from WhepAuthValidator
    // (spec 270 FR-002): the token is refused exactly like any other rejected
    // token, but this line is what lets an operator grep for which client id
    // it was. Debug, not Information (phase-6 review, N3): a revoked kiosk's
    // reconnect loop retries this refusal continuously, and every retry would
    // otherwise log at a level nobody filters out.
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Refused a token for '{ClientId}': its client was disabled after the token was minted (ADR-0160).")]
    public static partial void TokenRefusedAsRevoked(this ILogger logger, string clientId);
}
