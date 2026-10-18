using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 392 / issues #3012 and #3017, ADR-0144), captured green on <c>de9802b1</c> before
/// <see cref="StreamSourceUrl.From"/> learns to refuse an empty host and text after the closing bracket. What it accepts
/// and what it refuses with an older rule's message today it must keep, with the same message, from both
/// <see cref="StreamSourceUrl.From"/> and <see cref="StreamSourceUrl.FromStored"/> (except the raw-'@' rule, which
/// <c>FromStored</c> never had). None of these rows may be edited by the change.
/// </summary>
public class StreamSourceUrlAuthorityHostCharacterisationTests
{
    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string SchemeRefusal = "value must start with 'rtsp://'. (Parameter 'value')";

    public static TheoryData<string> Accepted =>
    [
        "rtsp://10.0.5.12/h264",
        "rtsp://cam:554/path?x=y",
        "RTSP://CAM/s",
        "rtsp://cam/s",
        "rtsp://@cam/s",
        "rtsp://[::1]:554/s",
        "rtsp://[fe80::1]/s",
        "rtsp://[fe80::1]",
        "rtsp://[fe80::1]:554/s",
        "rtsp://[fe80::1]:0554/s",
        "rtsp://[fe80::1]?q",
        "rtsp://[fe80::1]#f",
        "rtsp://[fe80::1%25eth0]/x",
        "rtsp://[fe80::1/64%25eth0]/s",
        "rtsp://[::ffff:10.0.0.1]/x",
    ];

    public static TheoryData<string> RefusedOnTheAtSignRuleOnly =>
    [
        "rtsp:///admin:4242#@cam",
        "rtsp:////admin:4242#@cam",
    ];

    public static TheoryData<string, string> RefusedOnAnOlderRuleByBoth => new()
    {
        { "rtsp://[fe80::1]:554x/s", UserInfoRefusal },
        { "rtsp://[fe80::1]\\x", UserInfoRefusal },
        { "rtsp://:554/cam", UserInfoRefusal },
        { "rtsp://u@[fe80::1]x/s", UserInfoRefusal },
        { "rtsp://[fe80::1]evil@cam/x", UserInfoRefusal },
        { "rtsp:\\\\[fe80::1]garbage/x", SchemeRefusal },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void From_keeps_accepting_the_accept_set(string address)
    {
        StreamSourceUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void FromStored_keeps_reading_back_the_accept_set(string address)
    {
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheAtSignRuleOnly))]
    public void From_keeps_refusing_an_empty_host_with_a_raw_at_sign_on_the_at_sign_rule_and_FromStored_reads_it(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(AtSignRefusal);
        refused.ToString().ShouldNotContain("4242", Case.Sensitive);
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRuleByBoth))]
    public void From_and_FromStored_keep_refusing_an_older_rule_violation_with_the_same_message(string address, string message)
    {
        var written = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));
        var stored = Should.Throw<ArgumentException>(() => StreamSourceUrl.FromStored(address));

        written.Message.ShouldBe(message);
        stored.Message.ShouldBe(message);
    }
}
