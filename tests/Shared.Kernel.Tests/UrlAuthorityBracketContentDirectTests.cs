namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 416 / issue #3023 item 6, direct rows. <see cref="UrlAuthority.BracketContentIsAddressOnly"/> is the one
/// bracket scan the audit asks; asserted here so Shared.Kernel's coverage does not depend on the architecture
/// suite's oracle comparison.
/// </summary>
public class UrlAuthorityBracketContentDirectTests
{
    [Theory]
    [InlineData("rtsp://[fe80::1]/x")]
    [InlineData("rtsp://[FE80::1]/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1]/x")]
    [InlineData("rtsp://[fe80::1]:554/x")]
    [InlineData("rtsp://u:p@[fe80::1]/x")]
    [InlineData("  rtsp://[fe80::1]/x  ")]
    public void An_address_only_bracketed_host_is_accepted(string url)
    {
        UrlAuthority.BracketContentIsAddressOnly(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp://u:p@[fe80::1/64%25eth0]/x")]
    [InlineData("rtsp://[fe80::1/64]/x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://[]@[FE80::1]")]
    [InlineData("rtsp://[x]@[::1]:/s?x#y")]
    [InlineData("rtsp://[fe80::1")]
    [InlineData("rtsp://[]/x")]
    [InlineData("rtsp://   ")]
    [InlineData("rtsp://cam/x")]
    [InlineData("")]
    public void A_host_that_is_not_only_an_address_is_refused(string url)
    {
        UrlAuthority.BracketContentIsAddressOnly(url).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_url_is_refused_by_the_guard()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.BracketContentIsAddressOnly(null!));
    }
}
