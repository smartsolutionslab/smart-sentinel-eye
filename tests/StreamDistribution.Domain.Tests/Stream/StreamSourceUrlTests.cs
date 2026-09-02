using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class StreamSourceUrlTests
{
    [Fact]
    public void From_accepts_an_rtsp_url()
    {
        StreamSourceUrl url = StreamSourceUrl.From("rtsp://camera-sim:8554/station-4");

        url.Value.ShouldBe("rtsp://camera-sim:8554/station-4");
    }

    [Fact]
    public void From_accepts_an_uppercase_scheme()
    {
        Should.NotThrow(() => StreamSourceUrl.From("RTSP://camera-sim:8554/station-4"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void From_rejects_blank_input(string? value)
    {
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From(value!));
    }

    [Fact]
    public void From_rejects_a_non_rtsp_scheme()
    {
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From("http://camera-sim:8554/station-4"));
    }

    [Fact]
    public void From_rejects_a_url_above_the_maximum_length()
    {
        string tooLong = "rtsp://host/" + new string('a', StreamSourceUrl.MaximumLength);

        Should.Throw<ArgumentException>(() => StreamSourceUrl.From(tooLong));
    }

    [Fact]
    public void From_rejects_a_user_password_segment()
    {
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From("rtsp://user:secret@camera-sim:8554/station-4"));
    }

    [Fact]
    public void Two_urls_with_the_same_value_are_equal()
    {
        StreamSourceUrl left = StreamSourceUrl.From("rtsp://camera-sim:8554/station-4");
        StreamSourceUrl right = StreamSourceUrl.From("rtsp://camera-sim:8554/station-4");

        left.ShouldBe(right);
    }
}
