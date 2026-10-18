using System.Globalization;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// The 84 UTF-16 code units spec 408 refuses in a camera URL (<c>char.IsControl</c> union <c>char.IsWhiteSpace</c>),
/// written out as ranges rather than computed from the predicate they pin, and the address templates the spec
/// measures them in. A row is a code point plus a position name, so a failure reads "U+0009 at path" and the raw
/// character never appears in a test name.
/// </summary>
internal static class UrlControlCharacterRows
{
    public static readonly IReadOnlyList<int> Members =
    [
        .. Enumerable.Range(0x00, 0x21),
        .. Enumerable.Range(0x7F, 0x22),
        0x1680,
        .. Enumerable.Range(0x2000, 0x0B),
        0x2028,
        0x2029,
        0x202F,
        0x205F,
        0x3000,
    ];

    public static readonly IReadOnlyList<int> TrimmedByTheParser = [0x09, 0x0A, 0x0D, 0x20];

    public static readonly IReadOnlyList<int> NonAsciiWhiteSpace =
    [
        0xA0,
        0x1680,
        .. Enumerable.Range(0x2000, 0x0B),
        0x2028,
        0x2029,
        0x202F,
        0x205F,
        0x3000,
    ];

    public static readonly IReadOnlyList<int> AsciiMembers = [.. Members.Where(member => member < 0x80)];

    public static readonly IReadOnlyList<int> WhatAHostEndAcceptsToday = [.. TrimmedByTheParser, .. NonAsciiWhiteSpace];

    public static readonly IReadOnlyList<string> FormatCharacterAddresses =
    [
        "rtsp://cam/li\x200Bve",
        "rtsp://cam/li\x200Eve",
        "rtsp://cam/li\x202Eve",
        "rtsp://cam/li\xFEFFve",
        "rtsp://cam/li\x00ADve",
        "rtsp://cam/li\x180Eve",
    ];

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        ["path"] = "rtsp://10.0.0.66/li{c}ve",
        ["query"] = "rtsp://10.0.0.66/live?q{c}x",
        ["fragment"] = "rtsp://10.0.0.66/live#f{c}x",
        ["path-end"] = "rtsp://10.0.0.66/live{c}",
        ["empty-userinfo"] = "rtsp://@cam/li{c}ve",
        ["after-bracketed-host"] = "rtsp://[fe80::1]/live{c}",
        ["zone-end"] = "rtsp://[fe80::1%25eth0{c}]/x",
        ["zone-middle"] = "rtsp://[fe80::1%25e{c}th0]/x",
        ["host-end"] = "rtsp://cam{c}",
        ["host-middle"] = "rtsp://cam{c}/live",
        ["host-octet"] = "rtsp://10.0.{c}0.66/live",
        ["host-start"] = "rtsp://{c}cam/live",
        ["port-middle"] = "rtsp://cam:85{c}54/live",
        ["port-end"] = "rtsp://cam:8554{c}/",
        ["after-bracket"] = "rtsp://[fe80::1]{c}/live",
        ["before-scheme"] = "{c}rtsp://cam/live",
        ["in-scheme-slashes"] = "rtsp:/{c}/cam/live",
        ["leading-run"] = "{c}rtsp://cam/s",
        ["trailing-path"] = "rtsp://cam/s{c}",
        ["trailing-ipv6-port"] = "rtsp://[fe80::1]:8554/s{c}{c}",
        ["both-edges"] = "{c}{c}rtsp://cam/s{c}",
    };

    public static string Address(string position, int codePoint) =>
        Templates[position].Replace("{c}", ((char)codePoint).ToString(), StringComparison.Ordinal);

    public static string Label(int codePoint, string position) =>
        string.Create(CultureInfo.InvariantCulture, $"U+{codePoint:X4} at {position}");

    public static TheoryData<int, string> Rows(params (IEnumerable<int> Members, string Position)[] groups)
    {
        TheoryData<int, string> data = [];

        foreach (var (members, position) in groups)
        {
            foreach (var member in members)
            {
                data.Add(member, position);
            }
        }

        return data;
    }

    public static (IEnumerable<int> Members, string Position) Every(string position) => (Members, position);
}
