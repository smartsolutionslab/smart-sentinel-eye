namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Commits the tracked changes and the integration events captured alongside
/// them, in one transaction (spec 021 FR-001).
///
/// <para>
/// A repository used to call <c>SaveChangesAsync</c> and then announce what it
/// had written. The gap between the two is where an integration event went
/// missing: the row was durable, the announcement was not, and nothing held a
/// copy. This is the replacement — one call, both or neither.
/// </para>
///
/// <para>
/// Deliberately one method. The concrete mechanism is a message bus with a
/// large surface, and a repository needs exactly one thing from it; depending
/// on the whole bus would put the messaging framework in nine persistence
/// classes and make each of them untestable without faking all of it.
/// </para>
/// </summary>
public interface ITransactionalCommit
{
    /// <summary>
    /// Persists the outstanding changes together with any integration events
    /// captured during this unit of work, then releases those events.
    ///
    /// <para>
    /// If this throws, nothing is committed and nothing is announced. If it
    /// returns, both happened — the announcement may not have reached its
    /// consumers yet, but it is durable and will be retried until it does.
    /// </para>
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
