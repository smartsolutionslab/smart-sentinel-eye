using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 408 / issue #3031 item 1, FR-001, red. <see cref="RtspUrl.From"/> refuses a control character or white
/// space anywhere in the address (the 84 code units of <c>char.IsControl</c> union <c>char.IsWhiteSpace</c>): a raw
/// line break, tab, NUL or space is copied into the request line a reader sends. The message is one constant that
/// says what to fix and never carries any part of the address; the rule is the last one, so an address an older
/// rule refuses keeps that rule's message (characterised in <see cref="RtspUrlControlCharacterCharacterisationTests"/>).
/// Percent-encoded forms and Unicode format characters are not raw characters and stay accepted. Receives the
/// retired row <c>rtsp://[fe80::1]/x </c> from <c>RtspUrlAuthorityHostAdversarialTests</c> (spec 408 A-2): a trailing
/// space was an accepted residual of spec 392, never a decision.
/// </summary>
public class RtspUrlControlCharacterTests
{
    private const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    public static TheoryData<string, string> Named => new()
    {
        { "rtsp://10.0.0.66/live\r\nX-Inject: 1", "X-Inject" },
        { "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live", "X-Inject" },
        { "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s", "CSeq" },
        { "rtsp://10.0.0.66/live?token=a\tSECRET", "SECRET" },
        { "rtsp://10.0.0.66/live\u0000SECRET", "SECRET" },
        { "rtsp://[fe80::1%25\u0000]/x", "fe80" },
        { "rtsp://10.0.0.66/live x", "live x" },
        { "rtsp://10.0.0.66/live\t", "10.0.0.66" },
        { "rtsp://[fe80::1]/x ", "fe80" },
        { "rtsp://dock-9 ", "dock-9" },
        { "RTSP://dock-9/x ", "dock-9" },
        { "rtsp://dock-9/x\r\n", "dock-9" },
        { "rtsp://dock-9/a\x00A0b", "dock-9" },
        { "rtsp://dock-9\x00A0/live", "dock-9" },
        { "rtsp://10.0.0.66/live\r\nX-Inject: 1 ", "X-Inject" },
    };

    public static TheoryData<int, string> ByPosition => UrlControlCharacterRows.Rows(
        UrlControlCharacterRows.Every("path"),
        UrlControlCharacterRows.Every("query"),
        UrlControlCharacterRows.Every("fragment"),
        UrlControlCharacterRows.Every("path-end"),
        UrlControlCharacterRows.Every("empty-userinfo"),
        UrlControlCharacterRows.Every("after-bracketed-host"),
        UrlControlCharacterRows.Every("zone-end"),
        UrlControlCharacterRows.Every("zone-middle"),
        (UrlControlCharacterRows.WhatAHostEndAcceptsToday, "host-end"),
        (UrlControlCharacterRows.NonAsciiWhiteSpace, "host-middle"),
        (UrlControlCharacterRows.NonAsciiWhiteSpace, "host-octet"),
        (UrlControlCharacterRows.NonAsciiWhiteSpace, "host-start"));

    [Theory]
    [MemberData(nameof(Named))]
    public void From_refuses_a_named_address_with_the_constant_message_and_echoes_none_of_it(string address, string marker)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(ControlOrWhiteSpaceRefusal);
        refused.ParamName.ShouldBe("value");
        refused.ToString().ShouldNotContain(marker, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(ByPosition))]
    public void From_refuses_every_member_at_every_position_it_accepts_today(int codePoint, string position)
    {
        var address = UrlControlCharacterRows.Address(position, codePoint);

        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address), UrlControlCharacterRows.Label(codePoint, position));

        refused.Message.ShouldBe(ControlOrWhiteSpaceRefusal, UrlControlCharacterRows.Label(codePoint, position));
    }

    [Fact]
    public void The_retired_trailing_space_row_is_refused_not_accepted()
    {
        Should.Throw<ArgumentException>(() => RtspUrl.From("rtsp://[fe80::1]/x "))
            .Message.ShouldBe(ControlOrWhiteSpaceRefusal);
    }

    [Fact]
    public void An_address_at_the_2048_character_limit_ending_in_a_space_is_refused_by_the_new_rule_not_the_length_cap()
    {
        var address = "rtsp://cam-7/" + new string('a', 2048 - 14) + " ";

        address.Length.ShouldBe(2048);
        Should.Throw<ArgumentException>(() => RtspUrl.From(address)).Message.ShouldBe(ControlOrWhiteSpaceRefusal);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Named.Cast<object[]>().Count().ShouldBe(15);
        ByPosition.Cast<object[]>().Count().ShouldBe((8 * 84) + 22 + (3 * 18));
    }
}
