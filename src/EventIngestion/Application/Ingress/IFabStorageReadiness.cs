using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Whether an event for a given fab can be stored at all (spec 019 FR-007).
///
/// <para>
/// Asked <b>before</b> the ingest channel is touched. The write path answers
/// <c>202 Accepted</c> the moment an envelope is queued and persists it later,
/// so by the time Postgres refuses the row the response is long gone and
/// nothing downstream can un-accept it. A check after the enqueue is not a
/// check; it is a slower way to lose the event.
/// </para>
///
/// <para>
/// The question is asked of the database rather than of the fab registry,
/// deliberately. The real precondition is that storage exists — not that a
/// plant exists — and those two differ for exactly as long as this feature's
/// window is open, which is the case that matters. It also keeps the registry
/// off the request path entirely, so ingest availability never depends on it.
/// </para>
/// </summary>
public interface IFabStorageReadiness
{
    /// <summary>
    /// <c>true</c> when an event for <paramref name="fab"/> can be stored now.
    ///
    /// <para>
    /// A negative answer must be re-checked against the source before it is
    /// returned, so a fab provisioned a minute ago is not refused by a stale
    /// cache. An error reaching the source <b>throws</b> — reporting "not
    /// provisioned" for a database problem would blame a gap that does not
    /// exist, and send someone to look in the wrong place.
    /// </para>
    /// </summary>
    Task<bool> IsReadyAsync(FabIdentifier fab, CancellationToken cancellationToken);
}
