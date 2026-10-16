namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 389 / issue #2999. The predicate that the write-side value objects and UrlRedaction.ToOrigin share, asserted
/// directly so a change to it is seen here and not only through its callers.
/// </summary>
public class UrlAuthorityTests
{
    [Theory]
    [InlineData("rtsp://admin:12345#@cam/s")]
    [InlineData("rtsp://admin:4711?@cam/s")]
    [InlineData("rtsp://admin:8080/x@cam/s")]
    [InlineData("rtsp://admin:1\\@cam/s")]
    [InlineData("rtsp:\\\\admin:1#@cam")]
    [InlineData("RTSP://ADMIN:12345#@CAM")]
    [InlineData("rtsp://12345#@cam/s")]
    [InlineData("rtsp://admin:12345#@[fd00::5]:8554/x")]
    [InlineData("rtsp://cam/s?mail=a@b")]
    [InlineData("rtsp://cam/a@b")]
    [InlineData("rtsp://cam/s#frag@x")]
    public void An_at_sign_after_the_first_terminator_follows_the_authority(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp://10.0.5.12/h264")]
    [InlineData("rtsp://cam:554/path?x=y")]
    [InlineData("rtsp://[::1]:554/s")]
    [InlineData("rtsp://cam/%40x")]
    [InlineData("rtsp://cam/s?mail=a%40b")]
    [InlineData("rtsp://admin:pass@cam/s")]
    [InlineData("rtsp://cam@")]
    [InlineData("rtsp://")]
    [InlineData("")]
    public void No_at_sign_after_the_authority_is_not_flagged(string url)
    {
        UrlAuthority.AtSignFollowsTheAuthority(url).ShouldBeFalse();
    }

    [Fact]
    public void A_null_url_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.AtSignFollowsTheAuthority(null!));
    }
}
