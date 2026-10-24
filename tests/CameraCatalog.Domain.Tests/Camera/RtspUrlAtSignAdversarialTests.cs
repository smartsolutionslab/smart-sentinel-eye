using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 389 / issue #2999, adversarial (T050). Variants of the issue's five forms that the written tests leave out:
/// userinfo-looking text after each terminator, mixed-case schemes, several '@', IPv6 hosts, control characters,
/// trailing whitespace and dots, and the 2048-character boundary. Every row refused on the raw-'@' rule must carry
/// the fix text and nothing of the address; every row refused on an older rule other than the userinfo rule must be
/// refused identically by <see cref="RtspUrl.FromStored"/>. A userinfo row is still refused by <see cref="RtspUrl.From"/>
/// with the same message but is read back by <see cref="RtspUrl.FromStored"/> (spec 450, owner-approved 2026-10-24).
/// The secret in most rows is numeric on purpose: only text the parser can read as a port survives as a misread
/// host and port; a non-numeric one makes the whole address unparseable, which the userinfo rule refused before
/// this rule existed (two older-rule rows pin that).
/// </summary>
public class RtspUrlAtSignAdversarialTests
{
    private const string Refusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string SchemeRefusal = "value must start with 'rtsp://'. (Parameter 'value')";

    private const string LengthRefusal = "value must be no more than 2048 character(s). (Parameter 'value')";

    private static readonly string AtSignAsCharacter2048 = "rtsp://cam/" + new string('a', 2048 - 12) + "@";

    private static readonly string AtSignAsCharacter2049 = "rtsp://cam/" + new string('a', 2048 - 11) + "@";

    public static TheoryData<string, string> RefusedOnTheAtSignRule => new()
    {
        { "RtSp://admin:4242#@cam", "4242" },
        { "rtsp://cam/s?u=admin:4242@x", "4242" },
        { "rtsp://cam/s#admin:4242@x", "4242" },
        { "rtsp://cam/s\\admin:4242@x", "4242" },
        { "rtsp://cam/admin:4242@x", "4242" },
        { "rtsp://admin:4242#@x@cam/s", "4242" },
        { "rtsp://admin:4242#x@@cam", "4242" },
        { "rtsp://cam/a@b@c", "a@b" },
        { "rtsp://[fd00::5]:8554/4242@y", "4242" },
        { "rtsp://admin:4242#@[fd00::5]:8554/x", "4242" },
        { "rtsp://admin:4242#@cam/s ", "4242" },
        { "rtsp://admin:4242#@cam\t", "4242" },
        { "rtsp://admin:4242#\t@cam", "4242" },
        { "rtsp://admin:4242#\0@cam/s", "4242" },
        { "rtsp://admin:4242#@cam./s", "4242" },
        { "rtsp:///admin:4242#@cam", "4242" },
        { "rtsp:////admin:4242#@cam", "4242" },
        { "rtsp://admin:4242#@c\u00E4m", "4242" },
        { "rtsp://admin:4242#@cam\u3000", "4242" },
        { "rtsp://cam/@", "cam/@" },
        { "rtsp://cam?@", "cam?@" },
        { "rtsp://cam#@", "cam#@" },
        { AtSignAsCharacter2048, "aaaaaaaa" },
    };

    public static TheoryData<string, string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://admin:4242@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:s3cr3t#@cam/s", UserInfoRefusal, "s3cr3t" },
        { "rtsp://admin:s3cr3t?@cam/s", UserInfoRefusal, "s3cr3t" },
        { "rtsp://admin:4242%23@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242%40cam/s", UserInfoRefusal, "4242" },
        { "rtsp://a:4242@c@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://[fd00::1]:4711@cam/s", UserInfoRefusal, "4711" },
        { "rtsp://admin:4242;@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242 @cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\0@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\n@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\\@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://\\admin:4242#@cam", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\u200E#@cam", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\uFF20cam/s", UserInfoRefusal, "4242" },
        { "rtsp://admin:4242\uFF03@cam/s", UserInfoRefusal, "4242" },
        { "rtsp://cam:65536/s", UserInfoRefusal, "65536" },
        { " rtsp://admin:4242#@cam", SchemeRefusal, "4242" },
        { "\u00A0rtsp://admin:4242#@cam", SchemeRefusal, "4242" },
        { "rtsp:\\\\admin:4242#@cam", SchemeRefusal, "4242" },
        { "http://admin:4242#@cam", SchemeRefusal, "4242" },
        { AtSignAsCharacter2049, LengthRefusal, "aaaaaaaa" },
    };

    public static TheoryData<string, string, string> RefusedOnAnOlderRuleOtherThanUserInfo =>
        Rows(userInfo: false);

    public static TheoryData<string, string, string> RefusedOnTheUserInfoRule =>
        Rows(userInfo: true);

    private static TheoryData<string, string, string> Rows(bool userInfo)
    {
        TheoryData<string, string, string> rows = [];

        foreach (var row in RefusedOnAnOlderRule.Cast<object[]>().Where(row => ((string)row[1] == UserInfoRefusal) == userInfo))
        {
            rows.Add((string)row[0], (string)row[1], (string)row[2]);
        }

        return rows;
    }

    public static TheoryData<string> Accepted =>
    [
        "rtsp://cam/%40x",
        "rtsp://cam/s?mail=a%40b",
        "rtsp://cam/s#%40",
        "rtsp://cam/s%23x",
        "rtsp://[fe80::1%25eth0]/s",
        "rtsp://[::1]:554/s",
        "rtsp://cam./s",
        "RtSp://CAM/s",
        "rtsp://cam/s;x",
    ];

    [Theory]
    [MemberData(nameof(RefusedOnTheAtSignRule))]
    public void From_refuses_a_variant_with_the_fix_text_and_nothing_of_the_address(string address, string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(Refusal);
        refused.ToString().ShouldNotContain(secret, Case.Sensitive);
        refused.ParamName.ShouldBe("value");
        refused.Data.Count.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheAtSignRule))]
    public void FromStored_reads_back_every_variant_From_refuses_on_the_at_sign_rule(string address, string secret)
    {
        _ = secret;

        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRuleOtherThanUserInfo))]
    public void From_and_FromStored_refuse_an_older_rule_violation_other_than_userinfo_identically(string address, string message, string secret)
    {
        var written = Should.Throw<ArgumentException>(() => RtspUrl.From(address));
        var stored = Should.Throw<ArgumentException>(() => RtspUrl.FromStored(address));

        written.Message.ShouldBe(message);
        stored.Message.ShouldBe(message);
        written.ToString().ShouldNotContain(secret, Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheUserInfoRule))]
    public void From_refuses_userinfo_with_its_message_and_FromStored_reads_it_back(string address, string message, string secret)
    {
        var written = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        written.Message.ShouldBe(message);
        written.ToString().ShouldNotContain(secret, Case.Sensitive);
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_percent_encoded_or_otherwise_harmless_variant_stays_accepted(string address)
    {
        RtspUrl.From(address).Value.ShouldBe(address);
        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Fact]
    public void The_older_rule_rows_are_split_without_losing_one()
    {
        RefusedOnAnOlderRuleOtherThanUserInfo.Cast<object[]>().Count().ShouldBe(5);
        RefusedOnTheUserInfoRule.Cast<object[]>().Count().ShouldBe(17);
        RefusedOnAnOlderRule.Cast<object[]>().Count().ShouldBe(22);
    }

    [Fact]
    public void The_length_boundary_rows_are_the_lengths_they_claim()
    {
        AtSignAsCharacter2048.Length.ShouldBe(RtspUrl.MaximumLength);
        AtSignAsCharacter2049.Length.ShouldBe(RtspUrl.MaximumLength + 1);
    }
}
