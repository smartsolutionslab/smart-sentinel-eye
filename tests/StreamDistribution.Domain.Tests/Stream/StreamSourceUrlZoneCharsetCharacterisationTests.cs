using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 411 / issue #3023, ADR-0144), captured green on <c>e5be6d72</c> before
/// <see cref="StreamSourceUrl.From"/> learns to refuse a non-printable zone and a punctuation-only host. The zones and hosts
/// of spec 411 §6 that stay accepted, including the rows pending the owner's questions Q1/Q2 (named as such), and the
/// rows an older rule refuses with its own message, which the new rules (placed last) must not take over. None of
/// these rows may be edited by the change.
/// </summary>
public class StreamSourceUrlZoneCharsetCharacterisationTests
{
    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    private const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    public static TheoryData<string> Accepted =>
    [
        "rtsp://[fe80::1%25eth0]:8554/x",
        "rtsp://[fe80::1%2512]/s",
        "rtsp://[fe80::1%eth0]/s",
        "rtsp://[fe80::1%09]/s",
        "rtsp://[fe80::1%25eth0:1]/s",
        "rtsp://[fe80::1%25eth0.100]/s",
        "rtsp://[fe80::1%25br-lan_1~x]/s",
        "rtsp://[fe80::1%25eth0!$&'()*+,;=]/s",
        "rtsp://[fe80::1%25eth0%3A1]/s",
        "rtsp://[::1%25%40x]/s",
        "rtsp://[::1%25x%3Fy]/s",
        "rtsp://[fe80::1%25eth0%2525]/s",
        "rtsp://[fe80::1%25eth0%zz]/s",
        "rtsp://[fe80::1%25]/s",
        "rtsp://[fe80::1/64%25eth0]/s",
        "rtsp://cam./x",
        "rtsp://cam_1/x",
        "rtsp://a-b/x",
        "rtsp://0/x",
        "rtsp://[::]/x",
        "rtsp://xn--kamera-mnchen-wob.local/live",
    ];

    public static TheoryData<string> AcceptedPendingTheOwnersQuestions =>
    [
        "rtsp://cam/liㅤve",
        "rtsp://cam/li‮ve",
    ];

    public static TheoryData<string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://[fe80::1%25eth0\r\nX]/s", ControlOrWhiteSpaceRefusal },
        { "rtsp://[fe80::1%25 x]/s", ControlOrWhiteSpaceRefusal },
        { "rtsp://[fe80::1%25%0D@x]/s", UserInfoRefusal },
        { "rtsp://[::1%25x?y]/s", DelimiterRefusal },
        { "rtsp://[fe80::1%25eth0]%0D/s", BracketTailRefusal },
        { "rtsp://.../x", UserInfoRefusal },
        { "rtsp://.cam/x", UserInfoRefusal },
        { "rtsp://..:554/x", UserInfoRefusal },
        { "rtsp://‎/p", EmptyHostRefusal },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    [MemberData(nameof(AcceptedPendingTheOwnersQuestions))]
    public void From_keeps_accepting_the_address_and_keeps_it_verbatim(string address)
    {
        StreamSourceUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    [MemberData(nameof(AcceptedPendingTheOwnersQuestions))]
    public void FromStored_keeps_reading_the_same_address_back(string address)
    {
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public void From_keeps_refusing_with_the_older_rule_s_message(string address, string refusal)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(refusal);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Accepted.Cast<object[]>().Count().ShouldBe(21);
        AcceptedPendingTheOwnersQuestions.Cast<object[]>().Count().ShouldBe(2);
        RefusedOnAnOlderRule.Cast<object[]>().Count().ShouldBe(9);
    }
}
