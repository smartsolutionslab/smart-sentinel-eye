namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 414 / issue #3031 item 7, ADR-0144), captured green before the three guarded
/// <see cref="Uri"/> reads (<see cref="UrlAuthority.TryReadHost"/>, <see cref="UrlAuthority.TryReadUserInfo"/> and
/// the parse inside <see cref="UrlRedaction.ToOrigin"/>) fold into one helper. Each row states its precondition on
/// <see cref="Uri"/> first, so a runtime that stops throwing fails the row rather than letting it pass vacuously.
/// None of these rows may be edited by the fold.
/// </summary>
public class UrlAuthorityGuardedReadCharacterisationTests
{
    private const string OnlyHostThrows = "rtsp://[1:2:3:4:5:6:7:8:/64]";

    private const string HostAndUserInfoThrow = "rtsp://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s";

    public static TheoryData<string> CreateThrows =>
    [
        "FILE://" + (char)0x200E + "/x",
        "file:/\\" + (char)0x200E,
    ];

    [Theory]
    [MemberData(nameof(CreateThrows))]
    public void Uri_TryCreate_throws_for_the_create_rows(string url)
    {
        Should.Throw<IndexOutOfRangeException>(() => Uri.TryCreate(url, UriKind.Absolute, out _));
    }

    [Theory]
    [MemberData(nameof(CreateThrows))]
    public void TryReadHost_answers_false_when_the_parser_throws_on_create(string url)
    {
        UrlAuthority.TryReadHost(url, out var host).ShouldBeFalse();
        host.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(CreateThrows))]
    public void TryReadUserInfo_answers_false_when_the_parser_throws_on_create(string url)
    {
        UrlAuthority.TryReadUserInfo(url, out var userInfo).ShouldBeFalse();
        userInfo.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(CreateThrows))]
    public void ToOrigin_answers_the_placeholder_when_the_parser_throws_on_create(string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void Uri_Host_throws_but_UserInfo_does_not_for_the_host_only_row()
    {
        Uri.TryCreate(OnlyHostThrows, UriKind.Absolute, out var uri).ShouldBeTrue();
        Should.Throw<IndexOutOfRangeException>(() => uri.Host);
        uri.UserInfo.ShouldBe(string.Empty);
    }

    [Fact]
    public void TryReadHost_answers_false_when_only_the_host_read_throws()
    {
        UrlAuthority.TryReadHost(OnlyHostThrows, out var host).ShouldBeFalse();
        host.ShouldBeNull();
    }

    [Fact]
    public void TryReadUserInfo_answers_empty_when_only_the_host_read_throws()
    {
        UrlAuthority.TryReadUserInfo(OnlyHostThrows, out var userInfo).ShouldBeTrue();
        userInfo.ShouldBe(string.Empty);
    }

    [Fact]
    public void ToOrigin_does_not_throw_when_only_the_host_read_throws()
    {
        UrlRedaction.ToOrigin(OnlyHostThrows).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void Uri_Host_and_UserInfo_both_throw_for_the_both_row()
    {
        Uri.TryCreate(HostAndUserInfoThrow, UriKind.Absolute, out var uri).ShouldBeTrue();
        Should.Throw<IndexOutOfRangeException>(() => uri.Host);
        Should.Throw<IndexOutOfRangeException>(() => uri.UserInfo);
    }

    [Fact]
    public void TryReadHost_answers_false_when_both_reads_throw()
    {
        UrlAuthority.TryReadHost(HostAndUserInfoThrow, out var host).ShouldBeFalse();
        host.ShouldBeNull();
    }

    [Fact]
    public void TryReadUserInfo_answers_false_when_both_reads_throw()
    {
        UrlAuthority.TryReadUserInfo(HostAndUserInfoThrow, out var userInfo).ShouldBeFalse();
        userInfo.ShouldBeNull();
    }

    [Fact]
    public void ToOrigin_does_not_throw_when_both_reads_throw()
    {
        UrlRedaction.ToOrigin(HostAndUserInfoThrow).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void Uri_TryCreate_refuses_text_that_is_not_a_url()
    {
        Uri.TryCreate("not a url", UriKind.Absolute, out _).ShouldBeFalse();
    }

    [Fact]
    public void Every_read_answers_false_or_the_placeholder_for_text_that_is_not_a_url()
    {
        UrlAuthority.TryReadHost("not a url", out var host).ShouldBeFalse();
        host.ShouldBeNull();
        UrlAuthority.TryReadUserInfo("not a url", out var userInfo).ShouldBeFalse();
        userInfo.ShouldBeNull();
        UrlRedaction.ToOrigin("not a url").ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void TryReadHost_reads_the_host_of_an_ordinary_address()
    {
        UrlAuthority.TryReadHost("rtsp://cam/s", out var host).ShouldBeTrue();
        host.ShouldBe("cam");
    }

    [Fact]
    public void TryReadUserInfo_reads_the_user_info_of_an_ordinary_address()
    {
        UrlAuthority.TryReadUserInfo("rtsp://u%40x@cam/s", out var userInfo).ShouldBeTrue();
        userInfo.ShouldBe("u%40x");
    }

    [Fact]
    public void ToOrigin_renders_scheme_host_and_a_non_default_port()
    {
        UrlRedaction.ToOrigin("rtsp://cam:554/x").ShouldBe("rtsp://cam:554");
    }
}
