namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 394 / issue #3023 items 1 and 5, red. <see cref="UrlRedaction.ToOrigin"/> reads the host from the parser,
/// which closes the brackets before it honours an '@', '?' or '#' written inside them. The origin then claims a
/// host the address did not unambiguously name (a parser that ends the userinfo at the last '@', or splits at the
/// first '?' or '#', reads another one). The origin is withheld: the placeholder, not a rendered host.
/// </summary>
public class UrlRedactionZoneDelimiterTests
{
    [Theory]
    [InlineData("rtsp://@[fe80::1%@10.0.0.66:8554]/live")]
    [InlineData("rtsp://@[::1%25@evil.com:554]/s")]
    [InlineData("rtsp://@[fe80::1%25eth0@]/s")]
    [InlineData("rtsp://[::1%25x?y]/s")]
    [InlineData("rtsp://[::1%25x#y]/s")]
    [InlineData("rtsp://[fe80::1%25a?b]/x")]
    [InlineData("rtsp://[fe80::1%x?y]/s")]
    [InlineData("rtsp://[::1%25x?y]")]
    [InlineData("rtsp://[fe80::1%25eth0#]:554/s")]
    [InlineData("rtsp://@[::1%25x?y]/s")]
    [InlineData("rtsp://user@[::1%25@evil.com:554]/s")]
    public void A_delimiter_inside_the_brackets_is_not_rendered_as_an_origin(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }
}
