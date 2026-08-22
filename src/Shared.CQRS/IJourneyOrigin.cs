namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Marks the work that begins a journey, so everything the journey goes on to
/// cause has something to point back at.
///
/// <para>
/// The abstraction exists for the same reason <see cref="IEventBus"/> and
/// <see cref="ILatencyBudget"/> do: the Application layer needs the behaviour
/// and must not reference the infrastructure that provides it.
/// `ServiceDefaults` implements this over the diagnostics it already owns.
/// </para>
///
/// <para>
/// <b>Why anything is needed at all.</b> The messaging layer propagates cause
/// across services and through the outbox already — measured over a
/// 4.3-second store-and-forward wait — but what it propagates is whatever work
/// is in progress when the publish happens. A message handler therefore passes
/// on its own cause for free. A background service draining a channel has no
/// work in progress to pass on, so everything it publishes begins as an orphan
/// and nothing downstream can be traced back to it. This supplies the one
/// thing that is missing, and nothing else.
/// </para>
/// </summary>
public interface IJourneyOrigin
{
    /// <summary>
    /// Begins the journey caused by one event, until the returned handle is
    /// disposed.
    ///
    /// <para>
    /// <b>One call is one journey.</b> Callers that process events in batches
    /// must call this per event: a single origin shared across a batch merges
    /// unrelated journeys, which still reads as correct from the effect end and
    /// makes "what did this event cause" unanswerable (spec 026 FR-006).
    /// </para>
    /// </summary>
    /// <returns>
    /// A handle that ends the origin when disposed. Never <see langword="null"/>,
    /// so callers need no branch — when nothing is listening the handle is inert
    /// rather than absent.
    /// </returns>
    IDisposable Begin(string name);
}
