namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 392 / issue #3012, red. <see cref="UrlRedaction.ToOrigin"/> reads the host from the parser, which drops
/// any text written between the closing ']' of an IPv6 host and the first '/', '?', '#' or '\' (other than
/// ':' and a port). The origin then claims a host the address did not name, and an audit of two such addresses
/// reads "same origin". The origin is withheld: the placeholder, not a rendered host.
/// </summary>
public class UrlRedactionBracketTailTests
{
    [Theory]
    [InlineData("rtsp://[fe80::1]%25eth0/x")]
    [InlineData("rtsp://[fe80::1]%eth0/x")]
    [InlineData("rtsp://[fe80::1]garbage.example/x")]
    [InlineData("rtsp://[fe80::1]garbage.example")]
    [InlineData("rtsp://[fe80::1]x:554/s")]
    [InlineData("rtsp://[fe80::1]]/x")]
    [InlineData("RTSP://[FE80::1]GARBAGE/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1]x/s")]
    [InlineData("rtsp://[fe80::1/64]x/s")]
    [InlineData("rtsp://[fe80::1]%20/s")]
    [InlineData("rtsp:\\\\[fe80::1]garbage/x")]
    [InlineData("http://[fe80::1]garbage/x")]
    [InlineData("rtsp://u@[fe80::1]x/s")]
    public void Text_after_the_closing_bracket_is_not_rendered_as_an_origin(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }
}
