using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 392 / issues #3012 and #3017, ADR-0144), captured green on <c>de9802b1</c> before
/// <see cref="RtspUrl.From"/> learns to refuse an empty host and text after the closing bracket. What it accepts
/// and what it refused with an older rule's message before that change it must keep, with the same message, from both
/// <see cref="RtspUrl.From"/> and <see cref="RtspUrl.FromStored"/>, except the raw-'@' rule, which
/// <c>FromStored</c> never had, and the userinfo rule, which <c>FromStored</c> no longer applies (spec 450,
/// owner-approved 2026-10-24: a stored row holding userinfo is read back and masked at the read boundary). No row's address or message is edited; the userinfo rows are split out and retargeted.
/// </summary>
public class RtspUrlAuthorityHostCharacterisationTests
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
        { "rtsp:\\\\[fe80::1]garbage/x", SchemeRefusal },
    };

    public static TheoryData<string, string> RefusedOnTheUserInfoRule => new()
    {
        { "rtsp://[fe80::1]:554x/s", UserInfoRefusal },
        { "rtsp://[fe80::1]\\x", UserInfoRefusal },
        { "rtsp://:554/cam", UserInfoRefusal },
        { "rtsp://u@[fe80::1]x/s", UserInfoRefusal },
        { "rtsp://[fe80::1]evil@cam/x", UserInfoRefusal },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void From_keeps_accepting_the_accept_set(string address)
    {
        RtspUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void FromStored_keeps_reading_back_the_accept_set(string address)
    {
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheAtSignRuleOnly))]
    public void From_keeps_refusing_an_empty_host_with_a_raw_at_sign_on_the_at_sign_rule_and_FromStored_reads_it(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(AtSignRefusal);
        refused.ToString().ShouldNotContain("4242", Case.Sensitive);
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRuleByBoth))]
    public void From_and_FromStored_keep_refusing_an_older_rule_violation_with_the_same_message(string address, string message)
    {
        var written = Should.Throw<ArgumentException>(() => RtspUrl.From(address));
        var stored = Should.Throw<ArgumentException>(() => RtspUrl.FromStored(address));

        written.Message.ShouldBe(message);
        stored.Message.ShouldBe(message);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheUserInfoRule))]
    public void From_keeps_refusing_userinfo_with_its_message_and_FromStored_reads_it_back(string address, string message)
    {
        var written = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        written.Message.ShouldBe(message);
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }
}
