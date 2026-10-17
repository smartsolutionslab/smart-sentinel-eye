using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 394 / issue #3023 items 1, 2 and 5, adversarial. Variants the written tests leave out: a delimiter at the
/// start or end of the zone, behind a prefix length, beside whitespace or an invisible character, after a raw '%'
/// zone, next to a percent-encoded one, and in a host with no path at all; unreadable hosts behind a query, a
/// fragment, a port, a zone or a leading '@'; userinfo that hides an unreadable host. Each row pins the message an
/// earlier rule may claim first, identical to <c>RtspUrl</c>, and no refusal carries the address. Addresses that
/// worked before must keep working: an empty userinfo, ordinary and percent-encoded zones, prefix lengths,
/// IPv4-mapped hosts, brackets and delimiters in the path, query and fragment.
/// </summary>
public class StreamSourceUrlZoneDelimiterAdversarialTests
{
    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string UnreadableHostRefusal =
        "value: must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const char ZeroWidthSpace = (char)0x200B;

    public static TheoryData<string, string, string> Refused => new()
    {
        { "RTSP://@[FE80::1%25ETH0@10.0.0.66]/s", DelimiterRefusal, "10.0.0.66" },
        { "rtsp://[fe80::1%25eth0?]/s", DelimiterRefusal, "eth0?" },
        { "rtsp://[fe80::1%25#]/s", DelimiterRefusal, "%25#" },
        { "rtsp://[::1%25x?y]:554/s", DelimiterRefusal, "x?y" },
        { "rtsp://[fe80::1%25eth0?x]?q#f", DelimiterRefusal, "eth0?x" },
        { "rtsp://[fe80::1/64%25x?y]/s", DelimiterRefusal, "x?y" },
        { "rtsp://[fe80::1%25x#y]#z", DelimiterRefusal, "x#y" },
        { "rtsp://[fe80::1%?evil.com]/s", DelimiterRefusal, "evil.com" },
        { "rtsp://[fe80::1%25%40x?y]/s", DelimiterRefusal, "x?y" },
        { "rtsp://[fe80::1%25 ?evil]/s", DelimiterRefusal, "evil" },
        { "rtsp://[fe80::1%25\t?evil]/s", DelimiterRefusal, "evil" },
        { "rtsp://[fe80::1%25" + ZeroWidthSpace + "#evil]/s", DelimiterRefusal, "evil" },
        { "rtsp://[fe80::1%25eth0#]", DelimiterRefusal, "eth0#" },
        { "rtsp://[fe80::1%25?]", DelimiterRefusal, "%25?" },
        { "rtsp://[::ffff:10.0.0.1%25@10.0.0.66]/s", UserInfoRefusal, "10.0.0.66" },
        { "rtsp://[fe80::1%25a@b@10.0.0.66]/s", UserInfoRefusal, "10.0.0.66" },
        { "rtsp://[fe80::1%25@]/s", UserInfoRefusal, "%25@" },
        { "rtsp://[fe80::1%25eth0@]", UserInfoRefusal, "eth0@" },
        { "rtsp://@[::1%25?@evil.com]/s", AtSignRefusal, "evil.com" },
        { "rtsp://[fe80::1%25x?y]x/s", BracketTailRefusal, "x?y" },
        { "rtsp://[fe80::1?x/64]/s", UserInfoRefusal, "fe80" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]?x", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]#x", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]:554/s", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:%25eth0/64]/s", UnreadableHostRefusal, "eth0" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64%25eth0]/s", UnreadableHostRefusal, "eth0" },
        { "rtsp://[1:2:3:4:5:6:7:8:%/64]", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[edda:7a56:BBCC:65D6:364E:99F2:e024:2de2:/0]?/", UnreadableHostRefusal, "edda" },
        { "rtsp://[::1:2:3:4:5:6:7:/0]", UnreadableHostRefusal, "1:2:3:4" },
        { "RTSP://@[1:2:3:4:5:6:7:8:/64]/s", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/0]x/s", UnreadableHostRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64]@evil.com/s", AtSignRefusal, "evil.com" },
        { "rtsp://[x]@[1:2:3:4:5:6:7:8:/64]x/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://:@[1:2:3:4:5:6:7:8:/64]/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://u@[1:2:3:4:5:6:7:8:/64]/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://%40@[1:2:3:4:5:6:7:8:/64]/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64?x]/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://[1:2:3:4:5:6:7:8:/64@x]/s", UserInfoRefusal, "1:2:3:4" },
        { "rtsp://[fe80::1%25x?y]/" + new string('a', 2048 - 23), DelimiterRefusal, "x?y" },
    };

    public static TheoryData<string> AddressesThatWorkedBeforeAndStillDo =>
    [
        "rtsp://@cam/s",
        "rtsp://@cam:554/s",
        "rtsp://[fe80::1%2540evil.com]/s",
        "rtsp://[fe80::1%253Fx]/s",
        "rtsp://[fe80::1%2523x]/s",
        "rtsp://[::1%25%3Fx]/s",
        "rtsp://[::1%25%23x]/s",
        "rtsp://[::1]/s?x#y",
        "rtsp://[::1]?#",
        "rtsp://[::1]#?",
        "rtsp://cam/s?a=1#f",
        "rtsp://10.0.0.1:554/s?q=1",
        "rtsp://[::ffff:10.0.0.1]:554/s",
        "rtsp://[::ffff:10.0.0.1%25eth0]/s",
        "rtsp://[1:2:3:4:5:6:7::/64]/s",
        "rtsp://[1:2:3:4:5:6:7:8/64]/s",
        "rtsp://[FE80::1%25ETH0]/S",
        "rtsp://[fe80::1%25eth0/64]:8554/x",
        "rtsp://[::1]/p?q=[a]#[b]",
        "rtsp://[::1]/%40?%23#%3F",
        "rtsp://cam.example.com/a/b?c#d",
        "rtsp://[::1]:8554/a]b?c]d#e]f",
        "rtsp://[fe80::1%25]/s",
        "rtsp://[fe80::1%]/s",
        "rtsp://[fe80::1%25eth0]/" + new string('a', 2048 - 24),
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public void From_refuses_the_variant_with_the_rule_that_claims_it_first_and_carries_none_of_it(
        string address,
        string refusal,
        string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.GetType().ShouldBe(typeof(ArgumentException));
        refused.Message.ShouldBe(refusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(secret, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(AddressesThatWorkedBeforeAndStillDo))]
    public void From_still_accepts_an_address_that_worked_before(string address)
    {
        StreamSourceUrl.From(address).Value.ShouldBe(address);
    }

    [Fact]
    public void The_boundary_rows_sit_exactly_at_the_maximum_length()
    {
        Refused.Cast<object[]>().Select(row => (string)row[0]).Max(address => address.Length).ShouldBe(2048);
        AddressesThatWorkedBeforeAndStillDo.Cast<object[]>().Select(row => (string)row[0]).Max(address => address.Length).ShouldBe(2048);
    }
}
