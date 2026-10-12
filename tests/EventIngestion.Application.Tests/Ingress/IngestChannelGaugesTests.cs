using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Ingress;

/// <summary>
/// Issue #2863: wires <see cref="BoundedIngestChannel.CurrentDepth"/> to an
/// <c>ObservableGauge</c> (ADR-0175's text already described it as "exposed
/// as a gauge", which was never actually true) and adds a second gauge
/// answering "how long since this channel last completed a write" — the
/// signal that lets a fully <b>stuck</b> channel be seen as a number that
/// keeps climbing, since <see cref="IngestChannelSaturation.RecordBlocked"/>
/// only ever reports a write that has already completed and therefore goes
/// silent, rather than large, on a write that never does.
///
/// <para>
/// Both gauges are expected on the one <see cref="IngestChannelSaturation"/>
/// meter, registered once per <see cref="BoundedIngestChannel"/> instance via
/// <see cref="IngestChannelSaturation.RegisterGauges"/> — see
/// <see cref="RecordedIngestChannelGauges"/> for how these tests observe what
/// a pull-based gauge reports.
/// </para>
/// </summary>
[Collection(IngestChannelSaturationCollection.Name)]
public class IngestChannelGaugesTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static IngestDelivery Delivery(string cycleId) =>
        new(
            new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From("munich"),
                Source.Plc,
                DeviceIdentifier.From("station-4"),
                Kind.From("PlcCycleStart"),
                OccurredAt.From(Now),
                Payload.From("{\"cycleId\":\"" + cycleId + "\"}")),
            NoCompletion.Instance);

    /// <summary>
    /// The depth gauge must read back exactly what sits queued, not a
    /// snapshot taken at registration time — it is read fresh every poll.
    /// </summary>
    [Fact]
    public async Task The_depth_gauge_reports_how_many_deliveries_are_currently_queued()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        using RecordedIngestChannelGauges gauges = new(channel);

        await channel.WriteAsync(Delivery("a"), CancellationToken.None);
        await channel.WriteAsync(Delivery("b"), CancellationToken.None);
        await channel.WriteAsync(Delivery("c"), CancellationToken.None);

        gauges.Poll();

        gauges.DepthReadings.ShouldHaveSingleItem().ShouldBe(channel.CurrentDepth);
        channel.CurrentDepth.ShouldBe(3);
    }

    /// <summary>
    /// The fast path: <see cref="BoundedIngestChannel.WriteAsync"/>'s
    /// <c>TryWrite</c> success case must still update the last-write marker,
    /// even though it never touches <see cref="IngestChannelSaturation"/>'s
    /// blocked-duration histogram.
    /// </summary>
    [Fact]
    public async Task The_time_since_last_write_gauge_reflects_a_write_that_did_not_block()
    {
        BoundedIngestChannel channel = new(capacity: 10);
        using RecordedIngestChannelGauges gauges = new(channel);

        await channel.WriteAsync(Delivery("a"), CancellationToken.None);
        gauges.Poll();

        double elapsed = gauges.TimeSinceLastWriteReadings.ShouldHaveSingleItem();
        elapsed.ShouldBeInRange(0d, 1d);
    }

    /// <summary>
    /// The slow path: a write that blocks and then completes must update the
    /// same marker the fast path updates. Ages the first write well past the
    /// generous assertion window before the second write blocks and
    /// completes, so a gauge that (wrongly) still reflected only the first
    /// write's timestamp would read back a much larger value than this
    /// assertion allows — this is what proves the blocked path's completion,
    /// not just the earlier fast write, set the marker.
    /// </summary>
    [Fact]
    public async Task The_time_since_last_write_gauge_reflects_a_write_that_had_to_block()
    {
        BoundedIngestChannel channel = new(capacity: 1);
        using RecordedIngestChannelGauges gauges = new(channel);

        await channel.WriteAsync(Delivery("1"), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        ValueTask pending = channel.WriteAsync(Delivery("2"), CancellationToken.None);
        await channel.ReadBatchAsync(1, CancellationToken.None);
        await pending;

        gauges.Poll();

        double elapsed = gauges.TimeSinceLastWriteReadings.ShouldHaveSingleItem();
        elapsed.ShouldBeLessThan(
            0.2,
            "the blocked write's own completion, not the earlier fast write, should have set the last-write marker");
    }

    /// <summary>
    /// The motivating case from the issue: a channel that is full with
    /// nothing draining it, so the next write blocks and never completes —
    /// the same shape as a persistence loop that has died while the only
    /// production caller passes <see cref="CancellationToken.None"/> and
    /// cannot be cancelled out of it. The gauge must keep reporting the age
    /// of the <b>last completed</b> write (growing, since nothing newer has
    /// completed) rather than reading back zero (which would wrongly claim
    /// the channel just wrote something) or timing the stuck write itself
    /// (which is <see cref="IngestChannelSaturation.RecordBlocked"/>'s job,
    /// and which, being unfinished, has produced no sample at all).
    /// </summary>
    [Fact]
    public async Task The_time_since_last_write_gauge_keeps_climbing_while_the_channel_is_stuck()
    {
        BoundedIngestChannel channel = new(capacity: 1);
        using RecordedIngestChannelGauges gauges = new(channel);

        await channel.WriteAsync(Delivery("1"), CancellationToken.None);

        // Deliberately not awaited, and nothing ever reads this channel: this
        // write blocks forever, so the point is to observe the gauge while it
        // is still in flight.
        _ = channel.WriteAsync(Delivery("2"), CancellationToken.None).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        gauges.Poll();

        double elapsed = gauges.TimeSinceLastWriteReadings.ShouldHaveSingleItem();
        elapsed.ShouldBeInRange(
            0.3,
            2.0,
            "a stuck channel must show this climbing from the last completed write, not sitting at zero");
    }
}
