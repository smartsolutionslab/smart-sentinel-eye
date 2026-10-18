namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 408 / issue #3031 item 1, FR-002 and A-1, ADR-0144), captured green on <c>a73df3f1</c>
/// before <see cref="UrlRedaction.ToOrigin"/> learns to withhold a control character or whitespace inside the
/// text. A run of such characters at the very start or end of the address is outside the text: the parser trims it,
/// it never reaches the rendered host, and four earlier specs pin those addresses as rendering their origin. Every
/// shape that already renders the placeholder keeps doing so, percent-encoded and Unicode format characters are
/// not raw characters, and <see cref="UrlRedaction.ToOrigin"/> never throws on any member at any position. None of
/// these rows may be edited by the change.
/// </summary>
public class UrlRedactionControlCharacterCharacterisationTests
{
    public static TheoryData<string, string> EdgeOnly => new()
    {
        { "rtsp://cam/x ", "rtsp://cam" },
        { " rtsp://cam/s", "rtsp://cam" },
        { "rtsp://10.0.0.66/live\t", "rtsp://10.0.0.66" },
        { "rtsp://cam\t", "rtsp://cam" },
        { "rtsp://[fe80::1]:8554/s\r\n", "rtsp://[fe80::1]:8554" },
        { "\r\n rtsp://cam/s \r\n", "rtsp://cam" },
        { "rtsp://cam/s\u0000", "rtsp://cam" },
        { "rtsp://cam/s\x00A0\x3000", "rtsp://cam" },
        { "  rtsp://[fe80::1%25eth0]/x  ", "rtsp://[fe80::1]" },
    };

    public static TheoryData<string, string> NotRawCharacters => new()
    {
        { "rtsp://cam/x%0D%0AX", "rtsp://cam" },
        { "rtsp://cam/x%20y", "rtsp://cam" },
        { "rtsp://cam/x%09y", "rtsp://cam" },
        { "rtsp://[fe80::1%25eth0]:8554/x", "rtsp://[fe80::1]:8554" },
        { "rtsp://cam/li\x200Bve", "rtsp://cam" },
        { "rtsp://cam/li\x200Eve", "rtsp://cam" },
        { "rtsp://cam/li\x202Eve", "rtsp://cam" },
        { "rtsp://cam/li\xFEFFve", "rtsp://cam" },
        { "rtsp://cam/li\x00ADve", "rtsp://cam" },
        { "rtsp://cam/li\x180Eve", "rtsp://cam" },
    };

    public static TheoryData<int, string> EdgeRunsRenderTheOriginForEveryMember => UrlControlCharacterRows.Rows(
        UrlControlCharacterRows.Every("path-end"),
        UrlControlCharacterRows.Every("after-bracketed-host"),
        UrlControlCharacterRows.Every("trailing-path"),
        UrlControlCharacterRows.Every("trailing-ipv6-port"),
        (UrlControlCharacterRows.TrimmedByTheParser, "host-end"),
        (UrlControlCharacterRows.TrimmedByTheParser, "before-scheme"),
        (UrlControlCharacterRows.TrimmedByTheParser, "leading-run"),
        (UrlControlCharacterRows.TrimmedByTheParser, "both-edges"));

    public static TheoryData<int, string> AlreadyThePlaceholder => UrlControlCharacterRows.Rows(
        UrlControlCharacterRows.Every("host-middle"),
        UrlControlCharacterRows.Every("host-octet"),
        UrlControlCharacterRows.Every("host-start"),
        UrlControlCharacterRows.Every("port-middle"),
        UrlControlCharacterRows.Every("port-end"),
        UrlControlCharacterRows.Every("after-bracket"),
        UrlControlCharacterRows.Every("in-scheme-slashes"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.TrimmedByTheParser), "host-end"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.TrimmedByTheParser), "before-scheme"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.TrimmedByTheParser), "leading-run"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.TrimmedByTheParser), "both-edges"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.AsciiMembers), "zone-end"),
        (UrlControlCharacterRows.Members.Except(UrlControlCharacterRows.AsciiMembers), "zone-middle"));

    [Theory]
    [MemberData(nameof(EdgeOnly))]
    public void An_address_whose_only_such_characters_sit_at_its_start_or_end_keeps_its_origin(string input, string origin)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(origin);
    }

    [Theory]
    [MemberData(nameof(NotRawCharacters))]
    public void A_percent_encoded_form_or_a_format_character_keeps_the_origin(string input, string origin)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(origin);
    }

    [Theory]
    [MemberData(nameof(EdgeRunsRenderTheOriginForEveryMember))]
    public void A_member_in_an_edge_run_keeps_the_origin_the_parser_reads(int codePoint, string position)
    {
        var input = UrlControlCharacterRows.Address(position, codePoint);

        UrlRedaction.ToOrigin(input).ShouldBe(ExpectedOrigin(position), UrlControlCharacterRows.Label(codePoint, position));
    }

    [Theory]
    [MemberData(nameof(AlreadyThePlaceholder))]
    public void A_shape_that_already_renders_the_placeholder_keeps_doing_so(int codePoint, string position)
    {
        var input = UrlControlCharacterRows.Address(position, codePoint);

        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, UrlControlCharacterRows.Label(codePoint, position));
    }

    [Fact]
    public void ToOrigin_does_not_throw_for_any_member_at_any_position()
    {
        var thrown = new List<string>();

        foreach (var position in new[]
        {
            "path", "query", "fragment", "path-end", "empty-userinfo", "after-bracketed-host", "zone-end", "zone-middle",
            "host-end", "host-middle", "host-octet", "host-start", "port-middle", "port-end", "after-bracket",
            "before-scheme", "in-scheme-slashes", "leading-run", "trailing-path", "trailing-ipv6-port", "both-edges",
        })
        {
            foreach (var member in UrlControlCharacterRows.Members)
            {
                try
                {
                    UrlRedaction.ToOrigin(UrlControlCharacterRows.Address(position, member));
                }
                catch (Exception escaped)
                {
                    thrown.Add(UrlControlCharacterRows.Label(member, position) + " -> " + escaped.GetType().Name);
                }
            }
        }

        thrown.ShouldBeEmpty();
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        EdgeOnly.Cast<object[]>().Count().ShouldBe(9);
        NotRawCharacters.Cast<object[]>().Count().ShouldBe(10);
        EdgeRunsRenderTheOriginForEveryMember.Cast<object[]>().Count().ShouldBe((4 * 84) + (4 * 4));
        AlreadyThePlaceholder.Cast<object[]>().Count().ShouldBe((7 * 84) + (4 * 80) + (2 * 50));
    }

    private static string ExpectedOrigin(string position) => position switch
    {
        "path-end" => "rtsp://10.0.0.66",
        "after-bracketed-host" => "rtsp://[fe80::1]",
        "trailing-ipv6-port" => "rtsp://[fe80::1]:8554",
        _ => "rtsp://cam",
    };
}
