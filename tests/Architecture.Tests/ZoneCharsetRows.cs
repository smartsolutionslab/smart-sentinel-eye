namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The rows of spec 411 §6 that the zone-charset (FR-001) and punctuation-only host (FR-002) rules refuse. Raw
/// characters are built from code points, never written into the source.
/// </summary>
internal static class ZoneCharsetRows
{
    public static readonly string[] EncodedOctets =
    [
        "%00", "%09", "%0A", "%0D", "%1F", "%20", "%7F", "%80", "%C2%85", "%C2%A0",
        "%E2%80%8E", "%E2%80%AE", "%EF%BB%BF", "%E3%85%A4", "%E2%A0%80",
    ];

    public static readonly int[] RawCodePoints =
    [
        0x200E, 0x202E, 0xFEFF, 0x200B, 0x3164, 0x115F, 0x1160, 0xFFA0, 0x2800, 0x00FC, 0xFF45,
    ];

    public static readonly (string Address, string Marker)[] Named =
    [
        ("rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live", "X-Inject"),
        ("rtsp://@[fe80::1%25eth0%0D]/s", "eth0"),
        ("rtsp://[fe80::1/64%25eth0%0D]/s", "eth0"),
        ("rtsp://[fe80::1/64%25eth0%0ASECRET]/s", "SECRET"),
        ("rtsp://[fe80::1%eth0%0D]/s", "eth0"),
        ("RTSP://[FE80::1%25ETH0%0A]/s", "ETH0"),
        ("rtsp://[fe80::1%25eth0%00SECRET]/s", "SECRET"),
    ];

    public static readonly string[] PunctuationOnlyHosts =
    [
        "rtsp://./", "rtsp://.", "rtsp://..", "rtsp://../live", "rtsp://..?x", "rtsp://..#x",
        "rtsp://-/x", "rtsp://_/x", "rtsp://-._/x", "RTSP://../x",
    ];

    public static string Encoded(string octets) => $"rtsp://[fe80::1%25eth0{octets}]/s";

    public static string Raw(int codePoint) => $"rtsp://[fe80::1%25eth0{char.ConvertFromUtf32(codePoint)}]/s";

    public static string Label(int codePoint) => $"U+{codePoint:X4} in zone";

    public static IEnumerable<string> EveryRefusedAddress() =>
        Named.Select(row => row.Address)
            .Concat(EncodedOctets.Select(Encoded))
            .Concat(RawCodePoints.Select(Raw))
            .Concat(PunctuationOnlyHosts);
}
