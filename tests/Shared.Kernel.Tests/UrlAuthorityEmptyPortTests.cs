namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 414 / issue #3031, post-green direct rows (T060). <see cref="UrlAuthority.WrittenPortIsEmpty"/> asserted
/// without the value objects in front of it, so a change to it is seen directly. Characterisation of current behaviour.
/// </summary>
public class UrlAuthorityEmptyPortTests
{
    [Fact]
    public void A_null_url_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.WrittenPortIsEmpty(null!));
    }

    [Theory]
    [InlineData("rtsp://10.0.0.66:/live")]
    [InlineData("rtsp://[::1]:/live")]
    [InlineData("rtsp://cam:")]
    [InlineData("rtsp://cam:/x")]
    [InlineData("rtsp://cam:?q")]
    [InlineData("rtsp://cam:#f")]
    [InlineData("rtsp://@cam:/x")]
    [InlineData("rtsp://cam.:/x")]
    [InlineData("RTSP://CAM:/S")]
    [InlineData("rtsp://cam:/x?a=b")]
    [InlineData("rtsp://xn--kamera-mnchen-wob.local:/live")]
    [InlineData("rtsp://[fe80::1]:")]
    [InlineData("rtsp://[fe80::1]:/")]
    [InlineData("rtsp://[fe80::1]:?q")]
    [InlineData("rtsp://[fe80::1]:#f")]
    [InlineData("rtsp://[fe80::1]:/s")]
    [InlineData("rtsp://[fe80::1]:/s?x#y")]
    [InlineData("rtsp://[fe80::1%25eth0]:/s")]
    [InlineData("rtsp://@[::1]:/x")]
    [InlineData("rtsp://@@[::1]:/x")]
    [InlineData("rtsp://[::ffff:10.0.0.1]:/s")]
    [InlineData("rtsp://[fe80::1/64]:/x")]
    [InlineData("rtsp://[fe80::1/64%25eth0]:/x")]
    [InlineData("rtsp://[1:2:3:4:5:6:7::/64]:/s")]
    [InlineData("rtsp://[7E8C::2214%251/64]:/s")]
    [InlineData("rtsp://cam\u200E:/p")]
    [InlineData(" rtsp://cam:/x")]
    public void A_colon_after_the_host_with_no_port_is_empty(string url)
    {
        UrlAuthority.WrittenPortIsEmpty(url).ShouldBeTrue(url);
    }

    [Theory]
    [InlineData("rtsp://10.0.0.66:554/live")]
    [InlineData("rtsp://10.0.0.66/live")]
    [InlineData("rtsp://cam:554/x")]
    [InlineData("rtsp://cam:0/x")]
    [InlineData("rtsp://cam:00/x")]
    [InlineData("rtsp://[::]:0/x")]
    [InlineData("rtsp://[fe80::1]:0554/s")]
    [InlineData("rtsp://[::1]:8554/live")]
    [InlineData("rtsp://cam/x:")]
    [InlineData("rtsp://cam/:/x")]
    [InlineData("rtsp://cam/x?a=:")]
    [InlineData("rtsp://cam/x#:")]
    [InlineData("rtsp://[1:2:3:4:5:6:7::/64]/s")]
    [InlineData("rtsp://[fe80::1/64]/x")]
    [InlineData("rtsp://cam")]
    [InlineData("RTSP://[2821::/64]/")]
    [InlineData(" rtsp://cam:554/x")]
    [InlineData("rtsp://[fe80::1]x:/s")]
    [InlineData("rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]/s:")]
    [InlineData("rtsp://[")]
    [InlineData("")]
    [InlineData("rtsp://")]
    public void A_port_or_no_colon_is_not_empty(string url)
    {
        UrlAuthority.WrittenPortIsEmpty(url).ShouldBeFalse(url);
    }

    // Refused upstream by the userinfo rule, so the empty port is not this rule's to report.
    [Fact]
    public void A_second_at_sign_before_the_host_is_not_an_empty_port_here()
    {
        UrlAuthority.WrittenPortIsEmpty("rtsp://a@[::1]@h:/x").ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://cam: ")]
    [InlineData("rtsp://cam:\r\n")]
    public void Trailing_whitespace_after_the_colon_is_not_an_empty_port_here(string url)
    {
        UrlAuthority.WrittenPortIsEmpty(url).ShouldBeFalse(url);
    }
}
