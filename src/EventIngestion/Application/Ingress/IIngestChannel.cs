namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Bounded channel that buffers deliveries between the broker ingress and the
/// persistence loop (spec 006 FR-021). The concrete implementation wraps a
/// <c>System.Threading.Channels.Channel&lt;T&gt;</c> with 5 000 slots.
///
/// <para>
/// <see cref="WriteAsync"/> blocks when the channel is full; that delays the
/// MQTT handler from returning, the broker stops receiving acknowledgements,
/// and queue depth absorbs the burst (FR-022).
/// </para>
///
/// <para>
/// Since spec 020 the channel carries an <see cref="IngestDelivery"/> rather
/// than a bare envelope, so the persistence loop can report each delivery's
/// outcome to whoever sent it. The HTTP write paths no longer use this channel
/// at all — they persist before answering, which is a promise they can keep
/// without one.
/// </para>
///
/// <para>
/// An item sitting in this channel is, for that reason, something <b>nobody has
/// been promised</b>: the broker has not been acknowledged and no caller has
/// been told anything. Losing the buffer to a crash therefore loses nothing the
/// system claimed to have — which is why it never needed to become durable.
/// </para>
/// </summary>
public interface IIngestChannel
{
    ValueTask WriteAsync(IngestDelivery delivery, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for at least one delivery and returns up to
    /// <paramref name="maximum"/> of them, so the loop can commit a batch and
    /// then acknowledge exactly that batch.
    ///
    /// <para>
    /// A batch rather than one at a time because acknowledgement now waits for
    /// the write: at the rate this path was sized for, a database round trip
    /// per message would not keep up (spec 020 FR-010). Returns an empty batch
    /// only when the channel is closed.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<IngestDelivery>> ReadBatchAsync(int maximum, CancellationToken cancellationToken);

    /// <summary>
    /// Takes whatever is already queued, up to <paramref name="maximum"/>,
    /// without waiting for anything to arrive.
    ///
    /// <para>
    /// This exists because a delivery being retried must not stop new arrivals
    /// being attempted (FR-009). The loop spends its backoff on a timer rather
    /// than blocked in <see cref="ReadBatchAsync"/>, then picks up whatever
    /// turned up in the meantime and retries the old and the new together. A
    /// loop that waited here would let one failing delivery hold every event
    /// behind it for the whole retry window.
    /// </para>
    /// </summary>
    IReadOnlyList<IngestDelivery> TakeAvailable(int maximum);

    int CurrentDepth { get; }
}
