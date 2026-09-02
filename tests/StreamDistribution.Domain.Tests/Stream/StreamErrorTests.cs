using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// The last failure a stream reported, or nothing where it has never failed.
///
/// <para>
/// Emptiness was already refused at both call sites in <c>Stream</c>. What the
/// type adds is the 1024-character bound, which lived only in
/// <c>StreamConfiguration</c> — and this is the property most likely to hit it,
/// because the values are gateway and transport errors whose length nobody
/// controls. Today an over-long one is accepted by the aggregate and rejected by
/// Postgres, which turns a stream health report into a failed write.
/// </para>
/// </summary>
public class StreamErrorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_stream_error_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => StreamError.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_stream_error_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        Action act = () => StreamError.From(new string('x', StreamError.MaximumLength + 1));

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_stream_error_at_the_exact_column_width_is_accepted()
    {
        string atLimit = new('x', StreamError.MaximumLength);

        StreamError.From(atLimit).Value.Length.ShouldBe(StreamError.MaximumLength);
    }

    [Fact]
    public void A_stream_error_is_stored_exactly_as_given()
    {
        const string reported = "RTSP 401 Unauthorized from upstream";

        StreamError.From(reported).Value.ShouldBe(reported);
    }

    [Fact]
    public void The_bound_matches_the_column_it_protects()
    {
        StreamError.MaximumLength.ShouldBe(1024);
    }

    /// <summary>
    /// The health watcher reports whatever the SFU hands it, from a background
    /// loop. <c>Truncating</c> clips rather than refuses so the state change
    /// survives a message nobody controls the length of.
    /// </summary>
    [Fact]
    public void An_error_from_an_external_system_is_clipped_rather_than_refused()
    {
        string tooLong = new('x', StreamError.MaximumLength + 500);

        StreamError clipped = StreamError.Truncating(tooLong);

        clipped.Value.Length.ShouldBe(StreamError.MaximumLength);
    }

    [Fact]
    public void An_error_within_the_bound_is_left_alone_by_the_truncating_factory()
    {
        const string reported = "RTSP 401 Unauthorized from upstream";

        StreamError.Truncating(reported).Value.ShouldBe(reported);
    }

    /// <summary>
    /// Clipping is not a licence to accept nothing: an empty report is a defect
    /// in the caller, not an over-long message.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_truncating_factory_still_refuses_an_empty_error(string input)
    {
        Action act = () => StreamError.Truncating(input);

        act.ShouldThrow<ArgumentException>();
    }
}
