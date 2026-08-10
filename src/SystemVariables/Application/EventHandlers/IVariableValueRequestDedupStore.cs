using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Dedup store for <c>SystemVariableValueRequestedV1</c> handler
/// (spec 007 FR-018). Wolverine's at-least-once outbox can deliver
/// the same V1 twice on a flaky network; the
/// <c>(fab, variableName, causingEventIdentifier)</c> triple is the
/// natural idempotency key.
///
/// <para>
/// The fab is part of the key, not context around it (spec 014). Two
/// fabs' rules reacting to the same ingested event share a causing event
/// identifier and a variable name, so keyed on the pair alone the second
/// fab's legitimate change is swallowed as a redelivery of the first.
/// That is the normal case once both fabs run rules on one trigger, not
/// an edge one.
/// </para>
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
    /// is the first time we've seen the triple (proceed) or <c>false</c>
    /// if we've already processed it (no-op).
    /// </summary>
    Task<bool> TryReserveAsync(
        FabIdentifier fab, string variableName, Guid causingEventIdentifier, CancellationToken cancellationToken);
}
