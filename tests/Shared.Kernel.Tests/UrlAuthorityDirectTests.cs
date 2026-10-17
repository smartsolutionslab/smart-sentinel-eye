namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 392 / issue #3012, post-green direct rows (T060). <see cref="UrlAuthority.TextFromTheAuthority"/>,
/// <see cref="UrlAuthority.IndexOfTheTerminator"/> and <see cref="UrlAuthority.TextFollowsTheClosingBracket"/> are the
/// pieces the value objects, <c>UrlRedaction.ToOrigin</c> and <c>AuditPayloadRedaction</c> share; asserted here so a
/// change to one is seen directly and not only through its callers. Characterisation of current behaviour.
/// </summary>
public class UrlAuthorityDirectTests
{
    [Theory]
    [InlineData("rtsp://cam/s", "cam/s")]
    [InlineData("RTSP://CAM", "CAM")]
    [InlineData("rtsp:///cam", "cam")]
    [InlineData("rtsp:////cam/s", "cam/s")]
    [InlineData("rtsp:\\\\cam\\s", "cam\\s")]
    [InlineData("rtsp://[fe80::1]x/s", "[fe80::1]x/s")]
    [InlineData("rtsp://u@cam:554/s", "u@cam:554/s")]
    [InlineData("cam/s", "cam/s")]
    [InlineData("//cam/s", "cam/s")]
    [InlineData("a+b.c-d://cam", "cam")]
    public void The_authority_text_starts_after_the_scheme_and_any_run_of_slashes(string url, string expected)
    {
        UrlAuthority.TextFromTheAuthority(url).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rtsp://")]
    [InlineData("rtsp:///")]
    [InlineData("rtsp:\\\\")]
    [InlineData("///")]
    public void The_authority_text_is_empty_when_nothing_follows(string url)
    {
        UrlAuthority.TextFromTheAuthority(url).IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1rtsp://cam")]
    [InlineData("r tsp://cam")]
    [InlineData("://cam")]
    public void A_prefix_that_is_not_a_scheme_is_not_stripped(string url)
    {
        UrlAuthority.TextFromTheAuthority(url).ToString().ShouldBe(url.TrimStart('/'));
    }

    [Theory]
    [InlineData("cam/s", 3)]
    [InlineData("cam?q", 3)]
    [InlineData("cam#f", 3)]
    [InlineData("cam\\s", 3)]
    [InlineData("/cam", 0)]
    [InlineData("cam/s?q#f", 3)]
    [InlineData("cam?q/s", 3)]
    [InlineData("cam", -1)]
    [InlineData("", -1)]
    [InlineData("cam:554;x%2F", -1)]
    public void The_terminator_is_the_first_slash_question_mark_hash_or_backslash(string authority, int expected)
    {
        UrlAuthority.IndexOfTheTerminator(authority).ShouldBe(expected);
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]%25eth0/x")]
    [InlineData("rtsp://[fe80::1]garbage/x")]
    [InlineData("rtsp://[fe80::1]garbage")]
    [InlineData("rtsp://[fe80::1]x:554/s")]
    [InlineData("rtsp://[fe80::1]]/x")]
    [InlineData("rtsp://[fe80::1]:554x/s")]
    [InlineData("rtsp://[fe80::1]:5 54/s")]
    [InlineData("rtsp://u@[fe80::1]x/s")]
    [InlineData("rtsp:\\\\[fe80::1]garbage/x")]
    [InlineData("[fe80::1]x/s")]
    public void Text_after_the_closing_bracket_other_than_a_port_is_flagged(string url)
    {
        UrlAuthority.TextFollowsTheClosingBracket(url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]/s")]
    [InlineData("rtsp://[fe80::1]")]
    [InlineData("rtsp://[fe80::1]:554/s")]
    [InlineData("rtsp://[fe80::1]:/s")]
    [InlineData("rtsp://[fe80::1]?q")]
    [InlineData("rtsp://[fe80::1]#f")]
    [InlineData("rtsp://[fe80::1]\\x")]
    [InlineData("rtsp://[fe80::1%25eth0]/x")]
    [InlineData("rtsp://cam/s")]
    [InlineData("rtsp://cam]x/s")]
    [InlineData("rtsp://[fe80::1/s")]
    [InlineData("rtsp://")]
    [InlineData("")]
    public void A_port_a_terminator_or_no_bracketed_host_is_not_flagged(string url)
    {
        UrlAuthority.TextFollowsTheClosingBracket(url).ShouldBeFalse();
    }

    [Fact]
    public void A_null_url_is_refused_by_the_closing_bracket_check()
    {
        Should.Throw<ArgumentNullException>(() => UrlAuthority.TextFollowsTheClosingBracket(null!));
    }
}
