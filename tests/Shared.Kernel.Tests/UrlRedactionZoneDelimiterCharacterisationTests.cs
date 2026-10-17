namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 394 / issue #3023, ADR-0144), captured green on <c>fbd4176b</c> before
/// <see cref="UrlRedaction.ToOrigin"/> learns to withhold '@', '?' and '#' inside the IPv6 brackets. An address
/// whose host the parser cannot read is already the placeholder, and it never throws; the accept set's bracket
/// rows keep their origins. None of these rows may be edited by the change.
/// </summary>
public class UrlRedactionZoneDelimiterCharacterisationTests
{
    public static TheoryData<string> UnreadableHost =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]",
    ];

    public static TheoryData<string> UnreadableUserInfo =>
    [
        "RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s",
        "rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s",
    ];

    [Theory]
    [MemberData(nameof(UnreadableHost))]
    public void An_address_whose_host_the_parser_cannot_read_is_already_the_placeholder(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(UnreadableUserInfo))]
    public void An_address_whose_userinfo_the_parser_cannot_read_is_already_the_placeholder(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[::1%25%40x]/s", "rtsp://[::1]")]
    [InlineData("rtsp://@[fe80::1]/x", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[::1%25x%3Fy]/s", "rtsp://[::1]")]
    [InlineData("rtsp://[fe80::1]:554/s", "rtsp://[fe80::1]:554")]
    [InlineData("rtsp://[fe80::1]?q", "rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]#f", "rtsp://[fe80::1]")]
    [InlineData("rtsp://@cam/s", "rtsp://cam")]
    public void The_accept_sets_bracket_rows_keep_their_origin(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        UnreadableHost.Cast<object[]>().Count().ShouldBe(5);
        UnreadableUserInfo.Cast<object[]>().Count().ShouldBe(2);
    }
}
