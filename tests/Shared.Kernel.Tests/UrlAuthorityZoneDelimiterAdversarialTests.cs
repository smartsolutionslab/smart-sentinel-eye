namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, adversarial, on the shared pieces directly.
/// <see cref="UrlAuthority.DelimiterInsideTheBrackets"/> fires for an '@', '?' or '#' between the brackets of the
/// host on either '@' reading and for no bracket outside the host (path, query, fragment, an unclosed or empty
/// pair). <see cref="UrlAuthority.TryReadHost"/> and <see cref="UrlAuthority.TryReadUserInfo"/> answer false rather
/// than throw for text the parser cannot create, cannot read, or throws on while creating. <see cref="UrlRedaction.ToOrigin"/>
/// withholds the origin for the delimiter shapes on every scheme spelling the authority scan recognises.
/// </summary>
public class UrlAuthorityZoneDelimiterAdversarialTests
{
    private static readonly string LeftToRightMark = ((char)0x200E).ToString();

    public static TheoryData<string> DelimiterInsideTheHostBrackets =>
    [
        "rtsp://[?]/s",
        "rtsp://[#]",
        "rtsp://@[::1%25?]/s",
        "rtsp://u@[::1%25#x]/s",
        "rtsp://@@[::1%25?x]/s",
        "RTSP://[fe80::1/64%25x?y]/s",
        "rtsp:///[::1%25x?y]/s",
        "rtsp:\\\\[::1%25x?y]/s",
        "http://[fe80::1%25a?b]/",
        "rtsp:[::1%25x#y]",
        " rtsp://[::1%25x?y]/s",
    ];

    public static TheoryData<string> NoDelimiterInsideTheHostBrackets =>
    [
        "rtsp://cam/[a?b]",
        "rtsp://cam?[@]",
        "rtsp://cam#[#]",
        "rtsp://[::1]?[a#b]",
        "rtsp://[::1]/p?q=[@]#[?]",
        "rtsp://[]",
        "rtsp://[fe80::1?x",
        "rtsp://[fe80::1%2540x]/s",
        "rtsp://[fe80::1%253Fx%2523y]/s",
        "rtsp://@cam/s",
        "rtsp://",
        "",
        "not a url",
    ];

    [Theory]
    [MemberData(nameof(DelimiterInsideTheHostBrackets))]
    public void A_delimiter_between_the_host_brackets_is_found(string url)
    {
        UrlAuthority.DelimiterInsideTheBrackets(url).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(NoDelimiterInsideTheHostBrackets))]
    public void A_bracket_outside_the_host_or_an_encoded_delimiter_is_not_a_delimiter_inside_the_brackets(string url)
    {
        UrlAuthority.DelimiterInsideTheBrackets(url).ShouldBeFalse();
    }

    // Blind spot, pinned: with no '@' before the '[' the scan reads only after an '@', so an '@' inside the brackets
    // with nothing bracketed after it yields no reading. It is safe only because the parser refuses the shape, which
    // is what this asserts; a parser that accepted it would need the scan to read from the authority's start too.
    [Theory]
    [InlineData("rtsp://[@]")]
    [InlineData("rtsp://[fe80::1%25eth0@]")]
    [InlineData("rtsp://[fe80::1%25@]:554/s")]
    [InlineData("rtsp://[::ffff:10.0.0.1%25@10.0.0.66]/s")]
    public void An_at_sign_inside_brackets_that_open_the_authority_is_missed_by_the_scan_and_refused_by_the_parser(string url)
    {
        UrlAuthority.DelimiterInsideTheBrackets(url).ShouldBeFalse();
        Uri.TryCreate(url, UriKind.Absolute, out _).ShouldBeFalse();
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(DelimiterInsideTheHostBrackets))]
    public void The_origin_of_a_delimiter_shape_is_withheld(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:/64]")]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:/64]?x")]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:%25eth0/64]/s")]
    [InlineData("rtsp://[edda:7a56:BBCC:65D6:364E:99F2:e024:2de2:/0]?/")]
    public void TryReadHost_answers_false_for_text_with_no_readable_host(string url)
    {
        UrlAuthority.TryReadHost(url, out var host).ShouldBeFalse();
        host.ShouldBeNull();
    }

    [Theory]
    [InlineData("rtsp://cam/s", "cam")]
    [InlineData("rtsp://@cam/s", "cam")]
    [InlineData("rtsp://[fe80::1%25eth0]:554/s", "[fe80::1]")]
    [InlineData("rtsp://[1:2:3:4:5:6:7::/64]/s", "[1:2:3:4:5:6:7:0]")]
    [InlineData("rtsp:///s", "")]
    [InlineData("rtsp:", "")]
    public void TryReadHost_reads_the_host_the_parser_reads(string url, string expected)
    {
        UrlAuthority.TryReadHost(url, out var host).ShouldBeTrue();
        host.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("rtsp://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s")]
    [InlineData("rtsp://[x]@[1:2:3:4:5:6:7:8:/64]x/s")]
    public void TryReadUserInfo_answers_false_for_text_with_no_readable_userinfo(string url)
    {
        UrlAuthority.TryReadUserInfo(url, out var userInfo).ShouldBeFalse();
        userInfo.ShouldBeNull();
    }

    [Theory]
    [InlineData("rtsp://[1:2:3:4:5:6:7:8:/64]", "")]
    [InlineData("rtsp://@cam/s", "")]
    [InlineData("rtsp://u%40x@cam/s", "u%40x")]
    [InlineData("rtsp://:@[1:2:3:4:5:6:7:8:/64]/s", ":")]
    public void TryReadUserInfo_reads_the_userinfo_the_parser_reads(string url, string expected)
    {
        UrlAuthority.TryReadUserInfo(url, out var userInfo).ShouldBeTrue();
        userInfo.ShouldBe(expected);
    }

    [Fact]
    public void Neither_read_throws_where_the_parser_throws_while_creating_the_uri()
    {
        var url = "file://[fe80::1" + LeftToRightMark + "]x/s";

        Should.NotThrow(() => UrlAuthority.TryReadHost(url, out _));
        Should.NotThrow(() => UrlAuthority.TryReadUserInfo(url, out _));
        Should.NotThrow(() => UrlAuthority.DelimiterInsideTheBrackets(url));
    }
}
