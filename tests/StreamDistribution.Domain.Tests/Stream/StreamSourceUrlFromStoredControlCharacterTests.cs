using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Characterisation (spec 408 / issue #3031 item 1, FR-003), captured green on <c>a73df3f1</c> and required to stay
/// green after: a row stored with a control character or whitespace in it is read back by
/// <see cref="StreamSourceUrl.FromStored"/> exactly as stored, at every position the write path is about to start
/// refusing. <see cref="StreamSourceUrl.From"/> is about to refuse every one of these; the read path must not, or one such
/// row takes the fab's camera list down.
/// </summary>
public class StreamSourceUrlFromStoredControlCharacterTests
{
    public static TheoryData<string> Named =>
    [
        "rtsp://10.0.0.66/live\r\nX-Inject: 1",
        "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live",
        "rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s",
        "rtsp://[fe80::1%25\u0000]/x",
        "rtsp://10.0.0.66/live x",
        "rtsp://10.0.0.66/live\t",
        "rtsp://[fe80::1]/x ",
        "rtsp://cam ",
        "RTSP://cam/x ",
        "rtsp://10.0.0.66/live?token=a\tSECRET",
        "rtsp://10.0.0.66/live\u0000SECRET",
    ];

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
    public void FromStored_reads_back_a_named_address_holding_such_a_character(string stored)
    {
        StreamSourceUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(ByPosition))]
    public void FromStored_reads_back_every_member_at_every_position_it_accepts_today(int codePoint, string position)
    {
        var stored = UrlControlCharacterRows.Address(position, codePoint);

        StreamSourceUrl.FromStored(stored).Value.ShouldBe(stored, UrlControlCharacterRows.Label(codePoint, position));
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        Named.Cast<object[]>().Count().ShouldBe(11);
        ByPosition.Cast<object[]>().Count().ShouldBe((8 * 84) + 22 + (3 * 18));
    }
}
