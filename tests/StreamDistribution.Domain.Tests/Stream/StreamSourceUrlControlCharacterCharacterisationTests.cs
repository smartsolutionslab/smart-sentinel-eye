using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 408 / issue #3031 item 1, ADR-0144), captured green on <c>a73df3f1</c> before
/// <see cref="StreamSourceUrl.From"/> learns to refuse a control character or whitespace anywhere. What it accepts today and
/// must keep accepting (ordinary, zoned, percent-encoded and Unicode format-character addresses), and what an older
/// rule refuses with its own message, it must keep refusing with that message: the new rule runs last, and the
/// length cap runs before everything. None of these rows may be edited by the change.
/// </summary>
public class StreamSourceUrlControlCharacterCharacterisationTests
{
    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    private const string SchemeRefusal = "value must start with 'rtsp://'. (Parameter 'value')";

    private const string LengthRefusal = "value must be no more than 2048 character(s). (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string DelimiterRefusal =
        "value: must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host (Parameter 'value')";

    public static TheoryData<string> Accepted =>
    [
        "rtsp://10.0.5.12/h264",
        "rtsp://[fe80::1%25eth0]:8554/x",
        "rtsp://cam/x%0D%0Ay",
        "rtsp://cam/x%20y",
        "rtsp://cam/x%09y",
        "rtsp://cam/x%0D%0Ay%20z",
        .. UrlControlCharacterRows.FormatCharacterAddresses,
    ];

    public static TheoryData<string, string> RefusedOnAnOlderRule => new()
    {
        { "rtsp://a b/x", UserInfoRefusal },
        { "rtsp://10.0.\u00000.66/live", UserInfoRefusal },
        { "rtsp://cam\u0000/live", UserInfoRefusal },
        { "rtsp://10.0.0.66:85 54/live", UserInfoRefusal },
        { " rtsp://cam/live", SchemeRefusal },
        { "\trtsp://cam/live", SchemeRefusal },
        { "rtsp://[fe80::1] /live", BracketTailRefusal },
        { "rtsp://[fe80::1]\u0000/live", BracketTailRefusal },
        { "rtsp://admin:4242#@cam/s ", AtSignRefusal },
        { "rtsp://[fe80::1%25 ?evil]/s", DelimiterRefusal },
        { "rtsp://[fe80::1]/x\r\nX-Inject: 1@evil", AtSignRefusal },
    };

    public static TheoryData<int, string> RefusedOnAnOlderRuleByPosition => UrlControlCharacterRows.Rows(
        UrlControlCharacterRows.Every("port-middle"),
        UrlControlCharacterRows.Every("port-end"),
        UrlControlCharacterRows.Every("after-bracket"),
        UrlControlCharacterRows.Every("before-scheme"),
        UrlControlCharacterRows.Every("in-scheme-slashes"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.WhatAHostEndAcceptsToday), "host-end"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.NonAsciiWhiteSpace), "host-middle"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.NonAsciiWhiteSpace), "host-octet"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.NonAsciiWhiteSpace), "host-start"));

    [Theory]
    [MemberData(nameof(Accepted))]
    public void From_keeps_accepting_the_address_and_keeps_it_verbatim(string address)
    {
        StreamSourceUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void FromStored_keeps_reading_the_same_address_back(string address)
    {
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRule))]
    public void From_keeps_refusing_with_the_older_rule_s_message_when_the_address_also_holds_such_a_character(
        string address,
        string refusal)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(refusal);
    }

    [Theory]
    [MemberData(nameof(RefusedOnAnOlderRuleByPosition))]
    public void From_keeps_refusing_with_the_older_rule_s_message_for_every_member_the_older_rule_already_catches(
        int codePoint,
        string position)
    {
        var address = UrlControlCharacterRows.Address(position, codePoint);

        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(OlderMessage(position), UrlControlCharacterRows.Label(codePoint, position));
    }

    [Fact]
    public void The_length_cap_answers_first_for_an_address_over_2048_characters_that_also_ends_in_a_space()
    {
        var address = "rtsp://cam/" + new string('a', 2048 - 11) + " ";

        address.Length.ShouldBe(2049);
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address)).Message.ShouldBe(LengthRefusal);
    }

    [Fact]
    public void The_length_cap_answers_first_for_an_address_over_2048_characters_that_holds_a_line_break()
    {
        var address = "rtsp://cam/\r\n" + new string('a', 2049 - 13);

        address.Length.ShouldBe(2049);
        Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address)).Message.ShouldBe(LengthRefusal);
    }

    [Fact]
    public void The_member_set_is_the_84_code_units_of_control_characters_and_white_space()
    {
        var computed = Enumerable.Range(0, char.MaxValue + 1)
            .Where(code => char.IsControl((char)code) || char.IsWhiteSpace((char)code))
            .ToList();

        UrlControlCharacterRows.Members.Count.ShouldBe(84);
        UrlControlCharacterRows.Members.Distinct().Count().ShouldBe(84);
        UrlControlCharacterRows.Members.Order().ShouldBe(computed);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Accepted.Cast<object[]>().Count().ShouldBe(12);
        RefusedOnAnOlderRule.Cast<object[]>().Count().ShouldBe(11);
        UrlControlCharacterRows.WhatAHostEndAcceptsToday.Count.ShouldBe(22);
        UrlControlCharacterRows.NonAsciiWhiteSpace.Count.ShouldBe(18);
        RefusedOnAnOlderRuleByPosition.Cast<object[]>().Count().ShouldBe((5 * 84) + 62 + (3 * 66));
    }

    private static string OlderMessage(string position) => position switch
    {
        "after-bracket" => BracketTailRefusal,
        "before-scheme" or "in-scheme-slashes" => SchemeRefusal,
        _ => UserInfoRefusal,
    };
}
