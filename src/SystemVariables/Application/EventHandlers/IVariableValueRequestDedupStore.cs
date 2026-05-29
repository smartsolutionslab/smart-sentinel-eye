namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Dedup store for <c>SystemVariableValueRequestedV1</c> handler
/// (spec 007 FR-018). Wolverine's at-least-once outbox can deliver
/// the same V1 twice on a flaky network; the
/// <c>(variableName, causingEventIdentifier)</c> pair is the
/// natural idempotency key.
///
/// <para>
/// The Postgres-backed impl in <c>SystemVariables.Infrastructure</c>
/// uses an <c>INSERT ... ON CONFLICT DO NOTHING</c> on a tiny dedup
/// table with a 7-day TTL.
/// </para>
/// </summary>
public interface IVariableValueRequestDedupStore
{
    /// <summary>
    /// Atomically inserts the dedup row; returns <c>true</c> if this
    /// is the first time we've seen the pair (proceed) or <c>false</c>
    /// if we've already processed it (no-op).
    /// </summary>
    Task<bool> TryReserveAsync(
        string variableName, Guid causingEventIdentifier, CancellationToken cancellationToken);
}
