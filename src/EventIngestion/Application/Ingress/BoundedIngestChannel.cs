using System.Threading.Channels;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Bounded channel buffering deliveries between the broker ingress and the
/// persistence loop (spec 006 FR-021). 5 000 slots per instance.
/// <c>FullMode = Wait</c> — <see cref="WriteAsync"/> blocks the caller, which
/// is what makes the MQTT subscriber stop taking deliveries when the channel
/// saturates and lets the broker hold queue depth (FR-022).
///
/// <para>
/// Single reader, so per-source order is preserved end to end: the broker
/// delivers in order, the channel is FIFO, and one loop drains it. Spec 020
/// batches that drain but does not add a second reader — the batch is committed
/// in order, so the guarantee survives.
/// </para>
/// </summary>
public sealed class BoundedIngestChannel : IIngestChannel
{
    public const int DefaultCapacity = 5_000;

    private readonly Channel<IngestDelivery> channel;

    public BoundedIngestChannel() : this(DefaultCapacity) { }

    public BoundedIngestChannel(int capacity)
    {
        channel = Channel.CreateBounded<IngestDelivery>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int CurrentDepth => channel.Reader.Count;

    public ValueTask WriteAsync(IngestDelivery delivery, CancellationToken cancellationToken)
    {
        Ensure.That(delivery).IsNotNull();
        return channel.Writer.WriteAsync(delivery, cancellationToken);
    }

    public async Task<IReadOnlyList<IngestDelivery>> ReadBatchAsync(
        int maximum, CancellationToken cancellationToken)
    {
        Ensure.That(maximum).AtLeast(1);

        // Waits for the first, then takes whatever else is already queued up to
        // the limit. Never waits *for* a batch to fill: a lone event at 3 a.m.
        // must not sit here until a second one arrives, which would spend the
        // latency budget on an optimisation for a load that is not happening.
        if (!await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            return [];
        }

        List<IngestDelivery> batch = [];
        while (batch.Count < maximum && channel.Reader.TryRead(out IngestDelivery? delivery))
        {
            batch.Add(delivery);
        }

        return batch;
    }

    public IReadOnlyList<IngestDelivery> TakeAvailable(int maximum)
    {
        Ensure.That(maximum).AtLeast(0);

        List<IngestDelivery> available = [];
        while (available.Count < maximum && channel.Reader.TryRead(out IngestDelivery? delivery))
        {
            available.Add(delivery);
        }

        return available;
    }
}
