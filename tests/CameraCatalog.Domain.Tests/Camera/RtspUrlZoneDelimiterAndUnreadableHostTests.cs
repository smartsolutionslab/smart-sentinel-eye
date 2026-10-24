using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, red. <see cref="RtspUrl.From"/> refuses '@', '?' and '#' inside the
/// brackets of an IPv6 address (a zone never holds one, and a parser that splits there reads another host), and an
/// address whose host or userinfo the parser cannot read is an <see cref="ArgumentException"/>, not the
/// <see cref="IndexOutOfRangeException"/> the BCL parser throws. Each refusal has its own message, identical in
/// <c>StreamSourceUrl</c>, and carries no part of the address.
/// </summary>
public class RtspUrlZoneDelimiterAndUnreadableHostTests
{
    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string UnreadableHostRefusal =
        "value: must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    public static TheoryData<string, string> DelimiterInsideTheBrackets => new()
    {
        { "rtsp://@[fe80::1%@10.0.0.66:8554]/live", "10.0.0.66" },
        { "rtsp://@[::1%25@evil.com:554]/s", "evil.com" },
        { "rtsp://@[fe80::1%25eth0@]/s", "eth0@" },
        { "rtsp://[::1%25x?y]/s", "x?y" },
        { "rtsp://[::1%25x#y]/s", "x#y" },
        { "rtsp://[fe80::1%25a?b]/x", "a?b" },
        { "rtsp://[fe80::1%x?y]/s", "x?y" },
        { "rtsp://[::1%25x?y]", "x?y" },
        { "rtsp://[fe80::1%25eth0#]:554/s", "eth0#" },
        { "rtsp://@[::1%25x?y]/s", "x?y" },
    };

    public static TheoryData<string> UnreadableHost =>
    [
        "rtsp://[1:2:3:4:5:6:7:8:/64]",
        "rtsp://[::1:2:3:4:5:6:7:/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8:/64]/live",
        "rtsp://[1:2:3:4:5:6:7:8:/64]x/s",
        "RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]",
    ];

    public static TheoryData<string> UnreadableUserInfo =>
    [
        "RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s",
        "rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s",
    ];

    [Theory]
    [MemberData(nameof(DelimiterInsideTheBrackets))]
    public void From_refuses_a_delimiter_inside_the_brackets_and_says_what_to_fix(string address, string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(DelimiterRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(secret, Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(UnreadableHost))]
    public void From_refuses_a_host_the_parser_cannot_read_with_an_ArgumentException(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(UnreadableHostRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain("1:2:3:4", Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(UnreadableUserInfo))]
    public void From_refuses_userinfo_the_parser_cannot_read_with_the_userinfo_message(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(UserInfoRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain("1:2:3:4", Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(UnreadableUserInfo))]
    public void FromStored_reads_back_userinfo_the_parser_cannot_read(string address)
    {
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        DelimiterInsideTheBrackets.Cast<object[]>().Count().ShouldBe(10);
        UnreadableHost.Cast<object[]>().Count().ShouldBe(5);
        UnreadableUserInfo.Cast<object[]>().Count().ShouldBe(2);
    }
}
