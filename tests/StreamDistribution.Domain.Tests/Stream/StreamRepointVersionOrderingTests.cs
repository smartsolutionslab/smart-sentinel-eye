using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 403 (#3008). The truth table of <c>Stream.RepointTo</c>: both versioned
/// compares versions (equal applies, a redelivery); incoming versioned over a
/// version-less record applies; incoming version-less is ordered by its stamp.
/// </summary>
public class StreamRepointVersionOrderingTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";
    private const string FirstUrl = "rtsp://camera-sim:8554/first";
    private const string SecondUrl = "rtsp://camera-sim:8554/second";

    [Fact]
    public void A_higher_version_applies_whatever_its_stamp_and_records_both_fields()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(10), Version(3), Clock()).ShouldBeTrue();

        var applied = stream.RepointTo(Url(SecondUrl), At(5), Version(4), Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceChangedAt.ShouldBe(At(5));
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(4));
    }

    [Fact]
    public void A_lower_version_is_ignored_whatever_its_stamp_and_changes_nothing()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(5), Version(4), Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(10), Version(3), Clock());

        applied.ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(FirstUrl);
        stream.SourceChangedAt.ShouldBe(At(5));
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(4));
    }

    [Fact]
    public void An_equal_version_applies_because_only_a_redelivery_can_repeat_one()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(5), Version(3), Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(1), Version(3), Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(3));
    }

    [Fact]
    public void A_versioned_change_over_a_record_without_a_version_applies_even_when_its_stamp_is_older()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(30), Option<SourceCameraVersion>.None, Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(0), Version(0), Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(0));
    }

    [Fact]
    public void A_far_future_legacy_stamp_cannot_pin_a_stream_against_a_versioned_change()
    {
        var stream = NewStream();
        stream.RepointTo(
            Url(FirstUrl),
            SourceChangedAt.From(DateTimeOffset.MaxValue),
            Option<SourceCameraVersion>.None,
            Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(0), Version(3), Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
    }

    [Fact]
    public void A_change_without_a_version_over_a_versioned_record_is_ordered_by_its_stamp_and_clears_the_version()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(5), Version(4), Clock());

        var newer = stream.RepointTo(Url(SecondUrl), At(10), Option<SourceCameraVersion>.None, Clock());

        newer.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
        stream.SourceChangedAt.ShouldBe(At(10));
        stream.SourceCameraVersion.ShouldBeNull();
    }

    [Fact]
    public void A_change_without_a_version_with_an_older_stamp_is_ignored_and_the_version_stays()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(5), Version(4), Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(1), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(FirstUrl);
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(4));
    }

    [Fact]
    public void A_change_without_a_version_with_the_same_stamp_applies()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(5), Version(4), Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(5), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe(SecondUrl);
    }

    [Fact]
    public void Two_changes_without_versions_are_ordered_by_their_stamps_as_before()
    {
        var stream = NewStream();
        stream.RepointTo(Url(FirstUrl), At(10), Option<SourceCameraVersion>.None, Clock());

        var applied = stream.RepointTo(Url(SecondUrl), At(1), Option<SourceCameraVersion>.None, Clock());

        applied.ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe(FirstUrl);
        stream.SourceCameraVersion.ShouldBeNull();
    }

    [Fact]
    public void A_retired_stream_still_throws_before_ordering_is_considered()
    {
        var stream = NewStream();
        stream.Retire(Clock());

        Should.Throw<InvalidOperationException>(
            () => stream.RepointTo(Url(FirstUrl), At(0), Version(1), Clock()));
    }

    [Fact]
    public void A_provisioned_stream_has_no_camera_version()
    {
        NewStream().SourceCameraVersion.ShouldBeNull();
    }

    private static Domain.Stream.Stream NewStream() =>
        new StreamBuilder().WithSourceUrl(Url(OriginalUrl)).Build();

    private static StreamSourceUrl Url(string value) => StreamSourceUrl.From(value);

    private static SourceChangedAt At(int minutes) => SourceChangedAt.From(Moment.AddMinutes(minutes));

    private static Option<SourceCameraVersion> Version(int value) =>
        Option<SourceCameraVersion>.Some(SourceCameraVersion.From(value));

    private static FixedClock Clock() => new(Moment);

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
