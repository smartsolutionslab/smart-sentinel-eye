using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 392 / issues #3012 and #3017, adversarial (T070). Variants the written tests leave out: empty hosts behind
/// whitespace, backslashes, casing and long slash runs; text after the closing ']' made of whitespace, control,
/// zero-width, fullwidth and percent-encoded characters; shapes an older rule must keep refusing with its own
/// message; addresses that worked before the change and must keep working; and the 2048-character boundary with
/// a ']' or a tail at the very end. Every refusal carries none of the address.
/// </summary>
public class RtspUrlAuthorityHostAdversarialTests
{
    private const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string SchemeRefusal = "value must start with 'rtsp://'. (Parameter 'value')";

    private const string LengthRefusal = "value must be no more than 2048 character(s). (Parameter 'value')";

    private static readonly string BracketAtCharacter2048 = "rtsp://[fe80::1]/" + new string('a', 2048 - 18) + "]";

    private static readonly string TailAtCharacter2048 = "rtsp://[fe80::1]" + new string('x', 2048 - 16);

    private static readonly string TailAtCharacter2049 = "rtsp://[fe80::1]" + new string('x', 2048 - 15);

    private static readonly string EightThousandCharacters = "rtsp://[fe80::1]" + new string(']', 8000);

    public static TheoryData<string, string> RefusedOnTheEmptyHostRule => new()
    {
        { "rtsp://////admin:4242@cam/s", "4242" },
        { "RtSp:///admin:4242@cam/s", "4242" },
        { "rtsp:///[fe80::1]garbage/x", "garbage" },
        { "rtsp:///[fe80::1]/x", "fe80" },
        { "rtsp:///%40", "%40" },
        { "rtsp:// ", "n/a" },
        { "rtsp://\t", "n/a" },
        { "rtsp:///\\", "n/a" },
    };

    public static TheoryData<string, string> RefusedOnTheBracketTailRule => new()
    {
        { "rtsp://[fe80::1]x", "n/a" },
        { "rtsp://[fe80::1]%25eth0", "eth0" },
        { "rtsp://[fe80::1]x:4242/s", "4242" },
        { "rtsp://[fe80::1]%3A4242/s", "4242" },
        { "rtsp://@[fe80::1]x/s", "n/a" },
        { "rtsp://[fe80::1][fe80::2]/s", "fe80::2" },
        { "rtsp://[fe80::1%25eth]0]/x", "eth" },
        { "rtsp://[fe80::1%25eth0%5D]x/x", "eth0" },
        { "rtsp://[fe80::1]\t/x", "n/a" },
        { "rtsp://[fe80::1] /x", "n/a" },
        { "rtsp://[fe80::1]\r/x", "n/a" },
        { "rtsp://[fe80::1]\n/x", "n/a" },
        { "rtsp://[fe80::1]\u00A0/x", "n/a" },
        { "rtsp://[fe80::1]\u200B/x", "n/a" },
        { "rtsp://[fe80::1]\uFEFF/x", "n/a" },
        { "rtsp://[fe80::1]\u0000/x", "n/a" },
        { "rtsp://[fe80::1]\u007F/x", "n/a" },
        { "rtsp://[fe80::1]\u0085/x", "n/a" },
        { "rtsp://[fe80::1]\u2028/x", "n/a" },
        { "rtsp://[fe80::1]\uFF3D/x", "n/a" },
        { "rtsp://[fe80::1]\uFF0Fx", "n/a" },
        { "rtsp://[fe80::1]\u2215x", "n/a" },
        { "rtsp://[fe80::1]\uFF20cam/x", "cam" },
        { "rtsp://[fe80::1]%2Fx", "n/a" },
        { "rtsp://[fe80::1]%40cam/x", "cam" },
        { "rtsp://[fe80::1]%5D/x", "n/a" },
        { "rtsp://[fe80::1]%5B/x", "n/a" },
        { "rtsp://[::ffff:10.0.0.1]x/s", "n/a" },
        { TailAtCharacter2048, "xxxxxxxx" },
    };

    public static TheoryData<string, string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://?admin:4242@cam", AtSignRefusal, "4242" },
        { "rtsp://#admin:4242@cam", AtSignRefusal, "4242" },
        { "rtsp:///?admin:4242@cam", AtSignRefusal, "4242" },
        { "rtsp://[fe80::1]/a@b", AtSignRefusal, "a@b" },
        { "rtsp://%20/", UserInfoRefusal, "%20" },
        { "rtsp://:554", UserInfoRefusal, "554" },
        { "rtsp://@", UserInfoRefusal, "n/a" },
        { "rtsp://@/", UserInfoRefusal, "n/a" },
        { "rtsp://[]", UserInfoRefusal, "n/a" },
        { "rtsp://[]/x", UserInfoRefusal, "n/a" },
        { "rtsp://\u0001/", UserInfoRefusal, "n/a" },
        { "rtsp://\tcam/x", UserInfoRefusal, "cam" },
        { "rtsp://\u200B[fe80::1]/x", UserInfoRefusal, "fe80" },
        { "rtsp://[fe80::1\u200B]/x", UserInfoRefusal, "fe80" },
        { "rtsp://\uFF3Bfe80::1\uFF3D/x", UserInfoRefusal, "fe80" },
        { "rtsp://%5Bfe80::1%5D/x", UserInfoRefusal, "fe80" },
        { "rtsp://[fe80::1%5D/x", UserInfoRefusal, "fe80" },
        { "rtsp://[fe80::1]\\x", UserInfoRefusal, "n/a" },
        { "rtsp://\\[fe80::1]x/s", UserInfoRefusal, "fe80" },
        { "rtsp://\\", UserInfoRefusal, "n/a" },
        { "rtsp://[fe80::1]:\uFF10/s", UserInfoRefusal, "fe80" },
        { "rtsp://[fe80::1]:+554/s", UserInfoRefusal, "554" },
        { "rtsp://[fe80::1]:554]/s", UserInfoRefusal, "554" },
        { "rtsp://[fe80::1]:554 /x", UserInfoRefusal, "554" },
        { "rtsp://[fe80::1]:65536/s", UserInfoRefusal, "65536" },
        { "rtsp://[x]@[fe80::1]/s", UserInfoRefusal, "fe80" },
        { "rtsp://[]@[fe80::1]x/s", UserInfoRefusal, "fe80" },
        { "rtsp://[1.2.3.4]/x", UserInfoRefusal, "1.2.3.4" },
        { "rtsp:[fe80::1]garbage/x", SchemeRefusal, "garbage" },
        { "rtsp:/[fe80::1]garbage/x", SchemeRefusal, "garbage" },
        { "rtsp:/\\[fe80::1]x/s", SchemeRefusal, "fe80" },
        { " rtsp://[fe80::1]x/s", SchemeRefusal, "fe80" },
        { TailAtCharacter2049, LengthRefusal, "xxxxxxxx" },
        { EightThousandCharacters, LengthRefusal, "]]]]]]]]" },
    };

    public static TheoryData<string> AddressesThatWorkedBeforeAndStillDo =>
    [
        "rtsp://[::1]:8554/a",
        "rtsp://host:554/[x]",
        "rtsp://cam/p]q",
        "rtsp://[FE80::ABCD]:8554/s",
        "rtsp://[fe80::abcd]:8554/s",
        "RTSP://[fe80::1]/x",
        "rtsp://[::ffff:10.0.0.1]:554/x",
        "rtsp://[::ffff:a00:1]/x",
        "rtsp://[0:0:0:0:0:0:0:1]/x",
        "rtsp://[::]:0/x",
        "rtsp://[fe80::1]:65535/s",
        "rtsp://[fe80::1]:00000000000000554/s",
        "rtsp://[fe80::1%25eth0]:554/s",
        "rtsp://[FE80::1%25ETH0]/s",
        "rtsp://[fe80::1%eth0]/x",
        "rtsp://[fe80::1%25eth0/64]/x",
        "rtsp://[fe80::1/64]/x",
        "rtsp://@[fe80::1]/s",
        "rtsp://@[fe80::1]:554/s",
        "rtsp://[fe80::1]?",
        "rtsp://[fe80::1]#",
        "rtsp://[fe80::1]/",
        "rtsp://[::1]?a]b",
        "rtsp://[::1]#]",
        "rtsp://[fe80::1]/%40",
        "rtsp://cam.:554/x",
        BracketAtCharacter2048,
    ];

    [Theory]
    [MemberData(nameof(RefusedOnTheEmptyHostRule))]
    public void An_empty_host_behind_casing_whitespace_backslashes_or_a_slash_run_is_refused_on_the_empty_host_rule(
        string address,
        string secret)
    {
        AssertRefused(address, EmptyHostRefusal, secret);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheBracketTailRule))]
    public void Whitespace_control_zero_width_fullwidth_or_encoded_text_after_the_closing_bracket_is_refused_on_the_bracket_rule(
        string address,
        string secret)
    {
        AssertRefused(address, BracketTailRefusal, secret);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public void A_shape_an_older_rule_refused_keeps_that_rule_s_message(string address, string refusal, string secret)
    {
        AssertRefused(address, refusal, secret);
    }

    [Theory]
    [MemberData(nameof(AddressesThatWorkedBeforeAndStillDo))]
    public void An_address_that_worked_before_the_change_is_still_accepted_and_kept_verbatim(string address)
    {
        RtspUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheEmptyHostRule))]
    [MemberData(nameof(RefusedOnTheBracketTailRule))]
    public void Every_address_the_new_rules_refuse_on_write_is_still_read_back_from_a_stored_row(
        string address,
        string secret)
    {
        _ = secret;

        RtspUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Fact]
    public void An_eight_thousand_character_address_is_refused_on_the_length_rule_without_scanning_for_long()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Should.Throw<ArgumentException>(() => RtspUrl.From(EightThousandCharacters));
        Should.Throw<ArgumentException>(() => RtspUrl.FromStored(EightThousandCharacters));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    private static void AssertRefused(string address, string refusal, string secret)
    {
        var thrown = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        thrown.Message.ShouldBe(refusal);
        if (secret != "n/a")
        {
            thrown.Message.ShouldNotContain(secret, Case.Insensitive);
        }
    }
}
