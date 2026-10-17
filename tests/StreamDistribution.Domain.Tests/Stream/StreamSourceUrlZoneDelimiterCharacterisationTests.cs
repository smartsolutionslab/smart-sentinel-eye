using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 394 / issue #3023 items 1, 2 and 5, ADR-0144), captured green on <c>fbd4176b</c> before
/// <see cref="StreamSourceUrl.From"/> learns to refuse '@', '?' and '#' inside the IPv6 brackets and to answer an
/// unreadable host with an <see cref="ArgumentException"/>. What it accepts, and what it refuses with an older
/// rule's message, it must keep, with the same message. <see cref="StreamSourceUrl.FromStored"/> keeps its accept set.
/// None of these rows may be edited by the change.
/// </summary>
public class StreamSourceUrlZoneDelimiterCharacterisationTests
{
    private const string AtSignRefusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    private const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    public static TheoryData<string> Accepted =>
    [
        "rtsp://[fe80::1%25eth0]/x",
        "rtsp://[fe80::1/64%25eth0]/s",
        "rtsp://[fe80::1%25eth0/64]/s",
        "rtsp://[fe80::1]/x",
        "rtsp://@[fe80::1]/x",
        "rtsp://@cam/s",
        "rtsp://[::1%25%40x]/s",
        "rtsp://[::1%25x%3Fy]/s",
        "rtsp://[fe80::1]?q",
        "rtsp://[fe80::1]#f",
        "rtsp://[fe80::1]:554/s",
    ];

    public static TheoryData<string> RefusedOnTheAtSignRule =>
    [
        "rtsp://@[::1%25x?@evil.com]/s",
        "rtsp://[::1%25x?y]/a@b",
    ];

    public static TheoryData<string> RefusedOnTheBracketTailRule => ["rtsp://[::1%25x?y]x/s"];

    public static TheoryData<string> RefusedOnTheUserInfoRule =>
    [
        "rtsp://user@[::1%25@evil.com:554]/s",
        "rtsp://[fe80::1%@10.0.0.66:8554]/live",
        "rtsp://[::1%25@evil.com:554]/s",
    ];

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
    [MemberData(nameof(RefusedOnTheAtSignRule))]
    public void From_keeps_refusing_a_raw_at_sign_after_the_authority_with_its_own_message_and_FromStored_reads_it(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(AtSignRefusal);
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheBracketTailRule))]
    public void From_keeps_refusing_text_after_the_closing_bracket_with_its_own_message_and_FromStored_reads_it(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));

        refused.Message.ShouldBe(BracketTailRefusal);
        StreamSourceUrl.FromStored(address).Value.ShouldBe(address);
    }

    [Theory]
    [MemberData(nameof(RefusedOnTheUserInfoRule))]
    public void From_and_FromStored_keep_refusing_userinfo_with_its_own_message(string address)
    {
        var written = Should.Throw<ArgumentException>(() => StreamSourceUrl.From(address));
        var stored = Should.Throw<ArgumentException>(() => StreamSourceUrl.FromStored(address));

        written.Message.ShouldBe(UserInfoRefusal);
        stored.Message.ShouldBe(UserInfoRefusal);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Accepted.Cast<object[]>().Count().ShouldBe(11);
        RefusedOnTheAtSignRule.Cast<object[]>().Count().ShouldBe(2);
        RefusedOnTheBracketTailRule.Cast<object[]>().Count().ShouldBe(1);
        RefusedOnTheUserInfoRule.Cast<object[]>().Count().ShouldBe(3);
    }
}
