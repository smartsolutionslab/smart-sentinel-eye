namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 392 / issue #3012, red. <c>Uri.TryCreate</c> skips leading whitespace and control characters, so an address
/// written with one in front still has its host read, but <see cref="UrlAuthority.TextFollowsTheClosingBracket"/>
/// used to read the written text from its first character and did not see the authority. Text after the closing
/// ']' hid behind the prefix and the origin was rendered with a host the address did not name.
/// </summary>
public class UrlRedactionLeadingWhitespaceBracketTailTests
{
    [Theory]
    [InlineData(" rtsp://[fe80::1]%25eth0/x")]
    [InlineData("\nrtsp://[fe80::1]x/s")]
    [InlineData("\trtsp://[fe80::1]garbage/x")]
    [InlineData("\rrtsp://[fe80::1]garbage/x")]
    [InlineData("  \t\n rtsp://[fe80::1]%eth0/x")]
    [InlineData(" rtsp://[fe80::1]x:554/s")]
    [InlineData(" rtsp://[fe80::1]]/x")]
    public void Text_after_the_closing_bracket_behind_a_leading_whitespace_or_control_prefix_is_not_rendered_as_an_origin(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData(" rtsp://[fe80::1]/x", "rtsp://[fe80::1]")]
    [InlineData("\nrtsp://[fe80::1]:554/s", "rtsp://[fe80::1]:554")]
    [InlineData("\trtsp://[fe80::1]:8554/s", "rtsp://[fe80::1]:8554")]
    [InlineData(" rtsp://cam/s", "rtsp://cam")]
    public void A_clean_host_behind_a_leading_whitespace_prefix_keeps_its_origin(string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected);
    }

    [Theory]
    [InlineData("\0rtsp://[fe80::1]garbage/x")]
    [InlineData("\u0001rtsp://[fe80::1]garbage/x")]
    [InlineData("\0rtsp://[fe80::1]/x")]
    public void A_prefix_the_parser_does_not_skip_leaves_the_address_unparseable(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }
}
