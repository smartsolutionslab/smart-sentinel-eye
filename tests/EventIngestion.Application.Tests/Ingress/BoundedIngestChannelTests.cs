using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Ingress;

[Collection(IngestChannelSaturationCollection.Name)]
public class BoundedIngestChannelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static IngestDelivery Delivery(string cycleId, IIngestCompletion? completion = null) =>
        new(
            new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From("munich"),
                Source.Plc,
                DeviceIdentifier.From("station-4"),
                Kind.From("PlcCycleStart"),
                OccurredAt.From(Now),
                Payload.From("{\"cycleId\":\"" + cycleId + "\"}")),
            completion ?? NoCompletion.Instance);

    /// <summary>
    /// Spec 020 T005. Per-source order is guaranteed by the channel being FIFO
    /// and single-reader, and the item type changing is the easiest way to lose
    /// it without noticing (FR-011).
    /// </summary>
    [Fact]
    public async Task Drains_in_FIFO_order_with_a_completion_attached()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        await channel.WriteAsync(Delivery("a"), CancellationToken.None);
        await channel.WriteAsync(Delivery("b"), CancellationToken.None);
        await channel.WriteAsync(Delivery("c"), CancellationToken.None);

        var batch =
            await channel.ReadBatchAsync(10, CancellationToken.None);

        batch.Select(delivery => delivery.Envelope.Payload.Value)
            .ShouldBe(["{\"cycleId\":\"a\"}", "{\"cycleId\":\"b\"}", "{\"cycleId\":\"c\"}"]);
    }

    [Fact]
    public async Task A_batch_carries_each_delivery_own_completion()
    {
        RecordingCompletion first = new();
        RecordingCompletion second = new();

        BoundedIngestChannel channel = new(capacity: 10);
        await channel.WriteAsync(Delivery("a", first), CancellationToken.None);
        await channel.WriteAsync(Delivery("b", second), CancellationToken.None);

        var batch =
            await channel.ReadBatchAsync(10, CancellationToken.None);

        // Acknowledging the batch must acknowledge each delivery's own sender —
        // a batch that reported the wrong set would release events that were
        // never stored, silently.
        await batch[0].Completion.StoredAsync(CancellationToken.None);

        first.Stored.ShouldBe(1);
        second.Stored.ShouldBe(0);
    }

    [Fact]
    public async Task Takes_no_more_than_the_batch_size()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        for (var i = 0; i < 7; i++)
        {
            await channel.WriteAsync(Delivery($"{i}"), CancellationToken.None);
        }

        var batch =
            await channel.ReadBatchAsync(3, CancellationToken.None);

        batch.Count.ShouldBe(3);
        channel.CurrentDepth.ShouldBe(4);
    }

    /// <summary>
    /// A lone event must not wait for company. Batching exists to amortise the
    /// write, not to hold the first event hostage until a second arrives —
    /// which would spend the latency budget on an optimisation for load that is
    /// not happening.
    /// </summary>
    [Fact]
    public async Task Returns_a_single_delivery_without_waiting_for_a_full_batch()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        await channel.WriteAsync(Delivery("alone"), CancellationToken.None);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
        var batch = await channel.ReadBatchAsync(200, cts.Token);

        batch.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WriteAsync_blocks_when_full_until_a_slot_frees()
    {
        BoundedIngestChannel channel = new(capacity: 1);
        await channel.WriteAsync(Delivery("1"), CancellationToken.None);

        // Blocks because the channel is full. This is the backpressure the
        // broker feels: the handler stops returning, the in-flight window
        // fills, and queue depth absorbs the burst (FR-022, ADR-0175).
        var pending = channel.WriteAsync(Delivery("2"), CancellationToken.None);
        pending.IsCompleted.ShouldBeFalse();

        await channel.ReadBatchAsync(1, CancellationToken.None);

        await pending;
        channel.CurrentDepth.ShouldBe(1);
    }

    /// <summary>
    /// ADR-0175's observability follow-up. <c>CurrentDepth</c> alone cannot
    /// distinguish a channel that is full and draining from one that is full
    /// and stuck — this is the signal that tells them apart, so a write that
    /// genuinely had to wait must record how long.
    /// </summary>
    [Fact]
    public async Task A_write_that_blocks_records_how_long_it_waited()
    {
        using RecordedIngestChannelSaturation recorded = new();
        BoundedIngestChannel channel = new(capacity: 1);
        await channel.WriteAsync(Delivery("1"), CancellationToken.None);

        var pending = channel.WriteAsync(Delivery("2"), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await channel.ReadBatchAsync(1, CancellationToken.None);
        await pending;

        var waited = recorded.BlockedDurationsMilliseconds.ShouldHaveSingleItem();
        waited.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The companion to the fact above: a write that finds capacity
    /// immediately did not block, and recording a zero would publish a
    /// measurement claiming saturation that never happened.
    /// </summary>
    [Fact]
    public async Task A_write_that_does_not_block_records_nothing()
    {
        using RecordedIngestChannelSaturation recorded = new();
        BoundedIngestChannel channel = new(capacity: 10);

        await channel.WriteAsync(Delivery("1"), CancellationToken.None);

        recorded.BlockedDurationsMilliseconds.ShouldBeEmpty("the write found capacity immediately");
    }

    private sealed class RecordingCompletion : IIngestCompletion
    {
        public int Stored { get; private set; }

        public int Abandoned { get; private set; }

        public Task StoredAsync(CancellationToken cancellationToken)
        {
            Stored++;
            return Task.CompletedTask;
        }

        public Task AbandonedAsync(CancellationToken cancellationToken)
        {
            Abandoned++;
            return Task.CompletedTask;
        }
    }
}
