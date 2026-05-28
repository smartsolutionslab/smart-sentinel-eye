using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Ingress;

public class BoundedIngestChannelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static EventEnvelope Envelope(string cycleId) =>
        new(
            EventIdentifier.New(),
            FabIdentifier.From("munich"),
            Source.Plc,
            DeviceIdentifier.From("station-4"),
            Kind.From("PlcCycleStart"),
            OccurredAt.From(Now),
            Payload.From("{\"cycleId\":\"" + cycleId + "\"}"));

    private static async Task<List<EventEnvelope>> DrainAsync(
        BoundedIngestChannel channel, int expected)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
        List<EventEnvelope> drained = new();
        try
        {
            await foreach (EventEnvelope envelope in channel.ReadAllAsync(cts.Token))
            {
                drained.Add(envelope);
                if (drained.Count == expected) break;
            }
        }
        catch (OperationCanceledException)
        {
            // The 2-second safety token tripped before we drained the expected count.
            // Surfacing the partial drain is enough — the assertion will fail loudly.
        }
        return drained;
    }

    [Fact]
    public async Task Drains_in_FIFO_order()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        channel.TryWrite(Envelope("a")).ShouldBeTrue();
        channel.TryWrite(Envelope("b")).ShouldBeTrue();
        channel.TryWrite(Envelope("c")).ShouldBeTrue();

        List<EventEnvelope> drained = await DrainAsync(channel, expected: 3);
        drained.Select(e => e.Payload.Value)
            .ShouldBe(["{\"cycleId\":\"a\"}", "{\"cycleId\":\"b\"}", "{\"cycleId\":\"c\"}"]);
    }

    [Fact]
    public void TryWrite_returns_false_when_the_channel_is_full()
    {
        BoundedIngestChannel channel = new(capacity: 2);
        channel.TryWrite(Envelope("1")).ShouldBeTrue();
        channel.TryWrite(Envelope("2")).ShouldBeTrue();

        // Third write must fail — bounded at 2.
        channel.TryWrite(Envelope("3")).ShouldBeFalse();
        channel.CurrentDepth.ShouldBe(2);
    }

    [Fact]
    public async Task WriteAsync_blocks_when_full_until_a_slot_frees()
    {
        BoundedIngestChannel channel = new(capacity: 1);
        channel.TryWrite(Envelope("1")).ShouldBeTrue();

        // Issue a WriteAsync that has to block because the channel is full.
        ValueTask pendingWrite = channel.WriteAsync(Envelope("2"), CancellationToken.None);
        pendingWrite.IsCompleted.ShouldBeFalse();

        // Drain the one existing item to free a slot.
        List<EventEnvelope> drained = await DrainAsync(channel, expected: 1);
        drained.ShouldHaveSingleItem();

        // The blocked WriteAsync must now complete.
        await pendingWrite;
        channel.CurrentDepth.ShouldBe(1);
    }
}
