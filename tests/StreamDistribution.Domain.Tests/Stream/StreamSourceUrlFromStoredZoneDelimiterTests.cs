using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 394 / issue #3023, US2a), captured green on <c>fbd4176b</c> and required to stay green
/// after: a row stored before '@', '?' and '#' inside the brackets became a refusal, or whose host the parser
/// cannot read, is read back by <see cref="StreamSourceUrl.FromStored"/> exactly as stored. <see cref="StreamSourceUrl.From"/> is
/// about to refuse every one of these; the read path must not, or one such row takes the fab's camera list down.
/// </summary>
public class StreamSourceUrlFromStoredZoneDelimiterTests
{
    public static TheoryData<string> DelimiterInsideTheBrackets =>
    [
        "rtsp://@[fe80::1%@10.0.0.66:8554]/live",
        "rtsp://@[::1%25@evil.com:554]/s",
        "rtsp://@[fe80::1%25eth0@]/s",
        "rtsp://[::1%25x?y]/s",
        "rtsp://[::1%25x#y]/s",
        "rtsp://[fe80::1%25a?b]/x",
        "rtsp://[fe80::1%x?y]/s",
        "rtsp://[::1%25x?y]",
        "rtsp://[fe80::1%25eth0#]:554/s",
        "rtsp://@[::1%25x?y]/s",
    ];

    public static TheoryData<string> UnreadableHost =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]",
    ];

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public void FromStored_reads_back_an_address_with_a_delimiter_inside_the_brackets(string stored)
    {
        StreamSourceUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(UnreadableHost))]
    public void FromStored_reads_back_an_address_whose_host_the_parser_cannot_read(string stored)
    {
        StreamSourceUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        DelimiterInsideTheBrackets.Cast<object[]>().Count().ShouldBe(10);
        UnreadableHost.Cast<object[]>().Count().ShouldBe(5);
    }
}
