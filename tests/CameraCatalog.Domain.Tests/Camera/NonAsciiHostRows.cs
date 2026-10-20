namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// The rows of spec 421 §6. <see cref="NonAscii"/> is what the plain-host ASCII rule (FR-001) refuses; the other sets
/// are the rows it must leave as they are. Every invisible or non-ASCII character is written as a code point escape,
/// never as the character itself, and every row carries a label that names it.
/// </summary>
internal static class NonAsciiHostRows
{
    public const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    public const string EmptyHostRefusal =
        "value: must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path (Parameter 'value')";

    public const string EmptyPortRefusal =
        "value: must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others (Parameter 'value')";

    public const string ControlOrWhiteSpaceRefusal =
        "value: must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20 (Parameter 'value')";

    public const string NonPrintableZoneRefusal =
        "value: must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others (Parameter 'value')";

    public const string BracketTailRefusal =
        "value: must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25 (Parameter 'value')";

    public const string NonAsciiHostRefusal =
        "value: must write the host in ASCII: a non-ASCII or invisible character there is shown as written but read differently, or dropped, by the parser. Write an internationalised name in its xn-- form (Parameter 'value')";

    public static readonly (string Label, string Address)[] Accepted =
    [
        ("an IPv4 host with a port", "rtsp://10.0.0.66:554/live"),
        ("an A-label host", "rtsp://xn--kamera-mnchen-wob.local/live"),
        ("a short A-label host", "rtsp://xn--caf-dma/x"),
        ("a trailing dot", "rtsp://cam./x"),
        ("an underscore", "rtsp://cam_1/x"),
        ("a hyphen", "rtsp://a-b/x"),
        ("a single digit host", "rtsp://0/x"),
        ("an upper-case host with a port", "rtsp://CAM:554/S"),
        ("an IPv6 host with an encoded zone", "rtsp://[fe80::1%25eth0]/s"),
        ("U+3164 in the path", "rtsp://cam/li\u3164ve"),
        ("U+202E in the path", "rtsp://cam/li\u202Eve"),
        ("U+00FC in the query", "rtsp://cam/x?q=\u00FC"),
        ("U+00FC in the fragment", "rtsp://cam/x#\u00FC"),
    ];

    public static readonly (string Label, string Address, string Refusal)[] OlderRule =
    [
        ("cam + U+00A0", "rtsp://cam\u00A0", ControlOrWhiteSpaceRefusal),
        ("cam + U+00A0 + path", "rtsp://cam\u00A0/p", ControlOrWhiteSpaceRefusal),
        ("cam + U+3000 + path", "rtsp://cam\u3000/x", ControlOrWhiteSpaceRefusal),
        ("cam + U+2028 + path", "rtsp://cam\u2028/x", ControlOrWhiteSpaceRefusal),
        ("U+200E as the whole host", "rtsp://\u200E/p", EmptyHostRefusal),
        ("U+00AD as the whole host", "rtsp://\u00AD/x", EmptyHostRefusal),
        ("cam + U+200E + empty port", "rtsp://cam\u200E:/x", EmptyPortRefusal),
        ("U+00FC in an IPv6 zone", "rtsp://[fe80::1%25\u00FC]/s", NonPrintableZoneRefusal),
        ("U+200E in an IPv6 zone", "rtsp://[fe80::1%25eth\u200E]/s", NonPrintableZoneRefusal),
        ("a CR LF injection in an IPv6 zone", "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live", NonPrintableZoneRefusal),
        ("U+200E after the closing bracket", "rtsp://[::1]\u200E/s", BracketTailRefusal),
        ("U+200E as the userinfo", "rtsp://\u200E@cam/p", UserInfoRefusal),
        ("U+00FC as the userinfo", "rtsp://\u00FC@cam/p", UserInfoRefusal),
        ("U+200E as the userinfo of an IPv6 host", "rtsp://\u200E@[::1]/s", UserInfoRefusal),
        ("U+200E after the port", "rtsp://cam:554\u200E/x", UserInfoRefusal),
        ("Arabic-Indic digits as the port", "rtsp://cam:\u0665\u0665\u0664/x", UserInfoRefusal),
        ("U+200E inside the brackets", "rtsp://[::1\u200E]/s", UserInfoRefusal),
        ("U+200E before the brackets", "rtsp://\u200E[::1]/s", UserInfoRefusal),
        ("a percent-encoded non-ASCII letter", "rtsp://caf%C3%A9/x", UserInfoRefusal),
        ("a percent-encoded ASCII letter", "rtsp://c%41m/x", UserInfoRefusal),
        ("an exclamation mark", "rtsp://ca!m/x", UserInfoRefusal),
    ];

    public static readonly (string Label, string Address)[] NonAscii =
    [
        Trailing(0x200E, "/p"), Trailing(0x202E, "/p"), Trailing(0xFEFF, "/p"), Trailing(0x200B, "/p"),
        Trailing(0x3164, "/p"), Trailing(0x2800, "/p"), Trailing(0x115F, "/p"), Trailing(0xFFA0, "/p"),
        Trailing(0x180E, "/x"), Trailing(0x034F, "/x"), Trailing(0xFE0F, "/x"), Trailing(0x00AD, "/x"),
        Trailing(0x2060, "/x"),
        ("U+2800 as the whole host", "rtsp://\u2800/p"),
        ("U+3164 as the whole host", "rtsp://\u3164/x"),
        ("U+200E before the host", "rtsp://\u200Ecam/p"),
        ("U+2800 before the host", "rtsp://\u2800cam/p"),
        ("U+200E inside the host", "rtsp://ca\u200Em/p"),
        ("cam + U+200E + port", "rtsp://cam\u200E:554/p"),
        ("cam + U+200E and nothing else", "rtsp://cam\u200E"),
        ("cam + U+200E + query", "rtsp://cam\u200E?q"),
        ("cam + U+200E + fragment", "rtsp://cam\u200E#f"),
        ("cam + U+200E after an empty userinfo", "rtsp://@cam\u200E/p"),
        ("cam + U+200E in upper case", "RTSP://CAM\u200E/X"),
        ("U+00FC in a host", "rtsp://kamera-m\u00FCnchen.local/live"),
        ("U+00E9 in a host", "rtsp://cam\u00E9ra/x"),
        ("U+00FC as the whole host", "rtsp://\u00FC/x"),
        ("katakana in a host", "rtsp://\u30AB\u30E1\u30E9.example/live"),
        ("fullwidth letters as the host", "rtsp://\uFF43\uFF41\uFF4D/live"),
        ("U+0130 in a host", "rtsp://CAM\u0130/x"),
        ("U+0666 in an IPv4 look-alike", "rtsp://10.0.0.\u0666/x"),
        ("fullwidth digits in an IPv4 look-alike", "rtsp://\uFF11\uFF10.0.0.1/x"),
        ("U+0301 combining mark", "rtsp://cam\u0301/x"),
        ("an emoji (a surrogate pair)", "rtsp://cam\uD83D\uDE00/x"),
        ("a lone low surrogate", "rtsp://cam\uDC00"),
        ("fullwidth solidus and equals sign", "rtsp://cam\uFF0Ftoken\uFF1DSECRET"),
        ("Cyrillic a in a host", "rtsp://SECRET-MARKER.c\u0430mera-sim:8554/x"),
        ("U+00FC in a repair-pass host", "rtsp://b\u00FCcher.example/kamera"),
    ];

    private static (string Label, string Address) Trailing(int codePoint, string suffix) =>
        ($"cam + U+{codePoint:X4}", $"rtsp://cam{char.ConvertFromUtf32(codePoint)}{suffix}");
}
