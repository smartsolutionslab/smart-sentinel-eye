namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 421 / issue #3023, post-green direct rows (T060). <see cref="UrlAuthority.NonAsciiCharacterInsideThePlainHost"/>
/// asserted without the value objects in front of it, so a change to it is seen directly.
/// </summary>
public class UrlAuthorityNonAsciiHostTests
{
    public static TheoryData<string, string> NonAscii()
    {
        TheoryData<string, string> rows = [];
        foreach (var (label, address) in NonAsciiHostRows.NonAscii)
        {
            rows.Add(label, address);
        }

        return rows;
    }

    public static TheoryData<string, string> Accepted()
    {
        TheoryData<string, string> rows = [];
        foreach (var (label, address) in NonAsciiHostRows.Accepted)
        {
            rows.Add(label, address);
        }

        return rows;
    }

    [Fact]
    public void A_null_url_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.NonAsciiCharacterInsideThePlainHost(null!));
    }

    [Theory]
    [MemberData(nameof(NonAscii))]
    public void A_non_ascii_character_in_the_plain_host_is_found(string label, string address)
    {
        UrlAuthority.NonAsciiCharacterInsideThePlainHost(address).ShouldBeTrue(label);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void An_ascii_host_is_not_flagged(string label, string address)
    {
        UrlAuthority.NonAsciiCharacterInsideThePlainHost(address).ShouldBeFalse(label);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1%25\u00FC]/s")]
    [InlineData("rtsp://cam/x?q=\u00FC")]
    [InlineData("rtsp://cam/x#\u00FC")]
    [InlineData("rtsp://")]
    [InlineData("rtsp://xn--kamera-mnchen-wob.local/live")]
    public void Text_outside_the_plain_host_is_not_flagged(string address)
    {
        UrlAuthority.NonAsciiCharacterInsideThePlainHost(address).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://@cam\u200E/p")]
    [InlineData(" rtsp://cam\u200E/p")]
    [InlineData("rtsp://cam\u200E")]
    public void An_empty_userinfo_or_leading_white_space_does_not_hide_the_host(string address)
    {
        UrlAuthority.NonAsciiCharacterInsideThePlainHost(address).ShouldBeTrue();
    }
}
