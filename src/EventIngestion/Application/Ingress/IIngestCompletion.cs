namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// How a delivery's outcome is reported back to whoever sent it (spec 020
/// FR-001). One of these travels with every envelope through the ingest
/// channel.
///
/// <para>
/// This exists because the system used to make its promise at the wrong moment.
/// A broker delivery was confirmed on arrival, which discarded the sender's copy
/// before anything had been stored — so a failed write, or a restart, lost an
/// event that had already been reported as accepted. The promise now waits for
/// the write, and this is the thing that carries it.
/// </para>
///
/// <para>
/// Deliberately says nothing about how the outcome is reported. For a broker
/// delivery it is an acknowledgement; a future durable buffer would report it
/// differently; and the direct HTTP paths do not use the channel at all any
/// more, because a request can simply answer after the write.
/// </para>
/// </summary>
public interface IIngestCompletion
{
    /// <summary>
    /// The event is durably stored — or was already, which is the same promise
    /// kept. Releases the sender's copy.
    /// </summary>
    Task StoredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The event will never be storable and has been recorded elsewhere
    /// (spec 020 FR-008). Also releases the sender's copy, because the
    /// alternative is redelivery forever — which is how one bad event takes
    /// ingestion down with it.
    ///
    /// <para>
    /// Only ever called after the delivery has been written somewhere an
    /// operator can find it. Releasing without recording would be the original
    /// defect wearing a bound.
    /// </para>
    /// </summary>
    Task AbandonedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// An envelope in flight, with the means to report what became of it.
/// </summary>
public sealed record IngestDelivery(EventEnvelope Envelope, IIngestCompletion Completion);

/// <summary>
/// For a sender with nothing to release — a delivery that arrived by a route
/// with no acknowledgement of its own. Reporting the outcome is a no-op rather
/// than a special case at every call site.
/// </summary>
public sealed class NoCompletion : IIngestCompletion
{
    public static readonly NoCompletion Instance = new();

    private NoCompletion() { }

    public Task StoredAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AbandonedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
