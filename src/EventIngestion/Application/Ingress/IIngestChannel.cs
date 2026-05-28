namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Bounded channel that buffers envelopes between ingress (MQTT
/// subscriber + HTTP endpoints) and the persistence loop. The
/// concrete implementation in <c>EventIngestion.Infrastructure</c>
/// wraps a <c>System.Threading.Channels.Channel&lt;T&gt;</c> with
/// 5 000 slots (spec 006 FR-021).
///
/// <para>
/// <see cref="TryWrite"/> returns <c>false</c> when the channel is
/// full — HTTP ingress uses this to short-circuit to 429
/// (FR-022). <see cref="WriteAsync"/> blocks until a slot frees —
/// the MQTT subscriber uses this so the broker holds queue depth
/// rather than NACK'ing.
/// </para>
/// </summary>
public interface IIngestChannel
{
    bool TryWrite(EventEnvelope envelope);

    ValueTask WriteAsync(EventEnvelope envelope, CancellationToken cancellationToken);

    IAsyncEnumerable<EventEnvelope> ReadAllAsync(CancellationToken cancellationToken);

    int CurrentDepth { get; }
}
