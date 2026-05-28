using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Bounded channel buffering envelopes between ingress and the
/// persistence loop (spec 006 FR-021). 5 000 slots per instance.
/// <c>FullMode = Wait</c> — <see cref="WriteAsync"/> blocks the
/// caller (this is what makes the MQTT subscriber stop ACKing
/// when the channel saturates per FR-022); <see cref="TryWrite"/>
/// returns <c>false</c> so HTTP ingress can short-circuit to 429.
/// </summary>
public sealed class BoundedIngestChannel : IIngestChannel
{
    public const int DefaultCapacity = 5_000;

    private readonly Channel<EventEnvelope> _channel;

    public BoundedIngestChannel() : this(DefaultCapacity) { }

    public BoundedIngestChannel(int capacity)
    {
        _channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int CurrentDepth => _channel.Reader.Count;

    public bool TryWrite(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return _channel.Writer.TryWrite(envelope);
    }

    public ValueTask WriteAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    public async IAsyncEnumerable<EventEnvelope> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out EventEnvelope? envelope))
            {
                yield return envelope;
            }
        }
    }
}
