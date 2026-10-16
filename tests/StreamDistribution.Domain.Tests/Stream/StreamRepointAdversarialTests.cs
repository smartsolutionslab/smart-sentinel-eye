using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 385, adversarial. The ordering guard at the edges of
/// <see cref="DateTimeOffset"/>, across offsets, and at the precision the
/// recorded instant comes back from Postgres with.
/// </summary>
public class StreamRepointAdversarialTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";
    private const string FirstUrl = "rtsp://camera-sim:8554/first";
    private const string SecondUrl = "rtsp://camera-sim:8554/second";

    [Fact]
    public void The_latest_representable_instant_refuses_everything_after_it_except_itself()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(DateTimeOffset.MaxValue), Clock()).ShouldBeTrue();

        stream.RepointTo(Url(SecondUrl), SourceChangedAt.From(DateTimeOffset.MaxValue.AddTicks(-1)), Clock()).ShouldBeFalse();
        stream.RepointTo(Url(SecondUrl), SourceChangedAt.From(Moment.AddYears(7000)), Clock()).ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(FirstUrl);

        stream.RepointTo(Url(SecondUrl), SourceChangedAt.From(DateTimeOffset.MaxValue), Clock()).ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
    }

    [Fact]
    public void The_earliest_representable_instant_applies_once_and_is_then_older_than_everything()
    {
        var stream = NewStream();

        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(DateTimeOffset.MinValue), Clock()).ShouldBeTrue();
        stream.RepointTo(Url(SecondUrl), SourceChangedAt.From(DateTimeOffset.MinValue.AddTicks(1)), Clock()).ShouldBeTrue();
        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(DateTimeOffset.MinValue), Clock()).ShouldBeFalse();

        stream.SourceUrl.Value.ShouldBe(SecondUrl);
    }

    [Fact]
    public void An_instant_near_the_upper_bound_written_with_a_negative_offset_normalises_without_overflow()
    {
        DateTimeOffset nearTheEnd = new(9999, 12, 31, 18, 0, 0, TimeSpan.FromHours(-5));

        var instant = SourceChangedAt.From(nearTheEnd);

        instant.Value.Offset.ShouldBe(TimeSpan.Zero);
        instant.Value.UtcTicks.ShouldBe(nearTheEnd.UtcTicks);
    }

    [Fact]
    public void Equal_instants_in_different_offsets_compare_equal_both_ways()
    {
        DateTimeOffset munich = new(2026, 10, 14, 11, 0, 0, TimeSpan.FromHours(2));
        var newYork = munich.ToOffset(TimeSpan.FromHours(-4));

        SourceChangedAt.From(munich).ShouldBe(SourceChangedAt.From(newYork));
        SourceChangedAt.From(munich).CompareTo(SourceChangedAt.From(newYork)).ShouldBe(0);
        (SourceChangedAt.From(munich) < SourceChangedAt.From(newYork)).ShouldBeFalse();
        (SourceChangedAt.From(newYork) < SourceChangedAt.From(munich)).ShouldBeFalse();
    }

    /// <summary>
    /// Npgsql 10.0.3 writes a timestamptz as whole microseconds since
    /// 2000-01-01 by integer division, so an instant after 2000 comes back
    /// truncated down (observed: ...09:00:00.0000007Z re-read as
    /// ...09:00:00.0000000Z). A redelivery of the same change, carrying the
    /// sub-microsecond ticks the re-read lost, must still count as equal or
    /// newer, never older.
    /// </summary>
    [Fact]
    public void A_redelivery_carrying_the_sub_microsecond_ticks_its_re_read_lost_still_applies()
    {
        var withTicks = Moment.AddTicks(7);
        var reReadFromPostgres = Moment;
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(reReadFromPostgres), Clock());

        var applied = stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(withTicks), Clock());

        applied.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Healthy")]
    [InlineData("Degraded")]
    [InlineData("Offline")]
    public void A_repoint_in_a_live_state_changes_neither_the_state_nor_raises_anything(string state)
    {
        var stream = NewStream();
        var error = StreamError.From("no frames");
        switch (state)
        {
            case "Healthy":
                stream.ReportHealthy(TranscodeMode.Passthrough, Clock());
                break;
            case "Degraded":
                stream.ReportDegraded(error, Clock());
                break;
            case "Offline":
                stream.ReportDegraded(error, Clock());
                stream.ReportOffline(error, Clock());
                break;
        }

        stream.ClearPendingEvents();

        stream.RepointTo(Url(SecondUrl), SourceChangedAt.From(Moment.AddMinutes(2)), Clock()).ShouldBeTrue();
        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(Moment.AddMinutes(1)), Clock()).ShouldBeFalse();

        stream.State.ShouldBe(StreamState.From(state));
        stream.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_retired_stream_never_repointed_refuses_and_records_nothing()
    {
        var stream = NewStream();
        stream.Retire(Clock());

        Should.Throw<InvalidOperationException>(() =>
            stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(Moment), Clock()));

        stream.SourceChangedAt.ShouldBeNull();
        stream.SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    [Fact]
    public void A_refused_older_change_leaves_both_the_address_and_the_instant_exactly_as_they_were()
    {
        var stream = NewStream();
        var recorded = SourceChangedAt.From(Moment.AddMinutes(5));
        stream.RepointTo(Url(SecondUrl), recorded, Clock());

        stream.RepointTo(Url(FirstUrl), SourceChangedAt.From(Moment.AddMinutes(5).AddTicks(-1)), Clock()).ShouldBeFalse();

        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceChangedAt.ShouldBeSameAs(recorded);
    }

    private static Domain.Stream.Stream NewStream() =>
        new StreamBuilder().WithSourceUrl(Url(OriginalUrl)).Build();

    private static StreamSourceUrl Url(string value) => StreamSourceUrl.From(value);

    private static FixedClock Clock() => new(Moment);

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
