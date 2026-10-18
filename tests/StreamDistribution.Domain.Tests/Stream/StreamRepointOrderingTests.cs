using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 385. A repoint is ordered by the instant the address was changed, not
/// by when it happened to be handled: a retried older change must not win back
/// over a newer one.
/// </summary>
public class StreamRepointOrderingTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";
    private const string FirstUrl = "rtsp://camera-sim:8554/first";
    private const string SecondUrl = "rtsp://camera-sim:8554/second";

    [Fact]
    public void A_stream_never_repointed_takes_any_change_and_records_when_it_was_made()
    {
        var stream = NewStream();

        var applied = stream.RepointTo(Url(FirstUrl), At(0), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(FirstUrl);
        stream.SourceChangedAt.ShouldBe(At(0));
    }

    [Fact]
    public void A_newer_change_applies_and_is_recorded()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(0), Option<SourceCameraVersion>.None, Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(1), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceChangedAt.ShouldBe(At(1));
    }

    [Fact]
    public void An_older_change_is_refused_and_changes_nothing()
    {
        var stream = NewStream();
        stream.RepointTo(Url(SecondUrl), At(1), Option<SourceCameraVersion>.None, Clock());

        var applied = stream.RepointTo(Url(FirstUrl), At(0), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceChangedAt.ShouldBe(At(1));
    }

    [Fact]
    public void A_change_made_at_the_recorded_instant_applies()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(0), Option<SourceCameraVersion>.None, Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(0), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
    }

    [Fact]
    public void A_newer_change_back_to_the_current_address_still_records_its_instant()
    {
        var stream = NewStream();

        var applied = stream.RepointTo(Url(OriginalUrl), At(2), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(OriginalUrl);
        stream.SourceChangedAt.ShouldBe(At(2));

        stream.RepointTo(Url(FirstUrl), At(1), Option<SourceCameraVersion>.None, Clock()).ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    [Fact]
    public void A_retired_stream_still_refuses_before_ordering_is_considered()
    {
        var stream = NewStream();
        stream.RepointTo(Url(SecondUrl), At(1), Option<SourceCameraVersion>.None, Clock());
        stream.Retire(Clock());

        Should.Throw<InvalidOperationException>(() =>
            stream.RepointTo(Url(FirstUrl), At(0), Option<SourceCameraVersion>.None, Clock()));
        Should.Throw<InvalidOperationException>(() =>
            stream.RepointTo(Url(FirstUrl), At(2), Option<SourceCameraVersion>.None, Clock()));

        stream.SourceChangedAt.ShouldBe(At(1));
    }

    [Fact]
    public void A_newly_provisioned_stream_has_no_recorded_change()
    {
        NewStream().SourceChangedAt.ShouldBeNull();
    }

    private static Domain.Stream.Stream NewStream() =>
        new StreamBuilder().WithSourceUrl(Url(OriginalUrl)).Build();

    private static StreamSourceUrl Url(string value) => StreamSourceUrl.From(value);

    private static SourceChangedAt At(int minutes) => SourceChangedAt.From(Moment.AddMinutes(minutes));

    private static FixedClock Clock() => new(Moment);

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
