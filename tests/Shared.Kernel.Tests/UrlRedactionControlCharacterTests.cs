namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 408 / issue #3031 item 1, FR-002 and A-1, red. <see cref="UrlRedaction.ToOrigin"/> renders the placeholder
/// for an address that holds a control character or whitespace inside its text, the same shapes the value objects
/// refuse. A run of such characters at the very start or end is outside the text (characterised in
/// <see cref="UrlRedactionControlCharacterCharacterisationTests"/>). Receives retired rows 4 to 6 of spec 408 A-2:
/// whitespace inside a userinfo that the parser consumed used to render <c>rtsp://cam</c>, and now renders the
/// placeholder, which fails closed and is reachable only for addresses no write accepted.
/// </summary>
public class UrlRedactionControlCharacterTests
{
    public static TheoryData<int, string> InteriorByPosition => UrlControlCharacterRows.Rows(
        UrlControlCharacterRows.Every("path"),
        UrlControlCharacterRows.Every("query"),
        UrlControlCharacterRows.Every("fragment"),
        UrlControlCharacterRows.Every("empty-userinfo"),
        (UrlControlCharacterRows.AsciiMembers, "zone-end"),
        (UrlControlCharacterRows.AsciiMembers, "zone-middle"));

    public static TheoryData<string> Named =>
    [
        "rtsp://10.0.0.66/live\r\nX-Inject: 1",
        "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live",
        "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s",
        "rtsp://10.0.0.66/live?token=a\tSECRET",
        "rtsp://10.0.0.66/live\u0000SECRET",
        "rtsp://10.0.0.66/live x",
        "rtsp://cam/a\x00A0b",
        " rtsp://cam/a b ",
        "\r\n rtsp://cam/a\tb\r\n",
        "rtsp://10.0.0.66/live\r\nX-Inject: 1 ",
    ];

    public static TheoryData<string> RetiredUserInfoRows =>
    [
        "rtsp://admin:12345 @cam/x",
        "rtsp://admin:12345\t@cam/x",
        "rtsp://admin:12345 @cam/s",
    ];

    [Theory]
    [MemberData(nameof(InteriorByPosition))]
    public void A_member_inside_the_text_renders_the_placeholder(int codePoint, string position)
    {
        var input = UrlControlCharacterRows.Address(position, codePoint);

        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, UrlControlCharacterRows.Label(codePoint, position));
    }

    [Theory]
    [MemberData(nameof(Named))]
    public void A_named_address_with_a_member_inside_the_text_renders_the_placeholder(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(RetiredUserInfoRows))]
    public void Whitespace_inside_a_consumed_userinfo_renders_the_placeholder_not_the_origin(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        InteriorByPosition.Cast<object[]>().Count().ShouldBe((4 * 84) + (2 * 34));
        Named.Cast<object[]>().Count().ShouldBe(10);
        RetiredUserInfoRows.Cast<object[]>().Count().ShouldBe(3);
        UrlControlCharacterRows.AsciiMembers.Count.ShouldBe(34);
    }
}
