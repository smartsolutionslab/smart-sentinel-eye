using System.Globalization;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 379 / issue #2989, adversarial pass over the host rule in <see cref="UrlRedaction.ToOrigin"/>.
/// The first group was meant to show that the host-character check is load-bearing (a backslash-spelled
/// authority followed by a later "//" moves the written-authority scan past the real host), but it does
/// not: deleting the host check leaves it green. The committed evidence that the host check is
/// load-bearing is UrlRedactionFileAuthorityTests. The rest pins the over-refusal boundary (non-ASCII outside
/// the authority still renders the origin) and the edges the K-rows do not reach. Marker checks are
/// case-insensitive because the host is lower-cased.
/// </summary>
public class UrlRedactionHostCharsetAdversarialTests
{
    private const string Marker = "SECRET";

    public static TheoryData<string, string> BackslashAuthorityRows()
    {
        int[] codePoints = [0x00A0, 0x00FC, 0x037E, 0xFF0F, 0xFF1D, 0x3002, 0x0130, 0x00AD, 0x200B, 0xFEFF, 0x1FEF];
        TheoryData<string, string> data = [];

        foreach (var scheme in new[] { "rtsp", "rtsps", "http" })
        {
            foreach (var separator in new[] { @"\\", @"/\", @"\/" })
            {
                foreach (var tail in new[] { "/x//y", "?u=rtsp://y", "#//y" })
                {
                    foreach (var codePoint in codePoints)
                    {
                        var input = $"{scheme}:{separator}cam{(char)codePoint}{Marker}{tail}";
                        data.Add($"U+{codePoint:X4} {scheme} {separator} {tail}", input);
                    }
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string, string> NonAsciiOutsideTheAuthorityRows()
    {
        (string Authority, string Origin)[] authorities =
        [
            ("cam", "rtsp://cam"),
            ("CAM.Example", "rtsp://cam.example"),
            ("cam:8554", "rtsp://cam:8554"),
            ("10.0.5.12", "rtsp://10.0.5.12"),
            ("[fd00::5]:8554", "rtsp://[fd00::5]:8554"),
        ];
        string[] suffixes =
        [
            "/stream/ünïcode?x=ß",
            "/x?token=ß#ü",
            "?ü",
            "#ü",
            "/x#\u202Egnp.exe",
            "/x\u3000",
            "/x\u00A0",
            "/x\uFEFF",
            "/x\u2029",
            "/ü//kämera/x",
            "/x／SECRET",
        ];
        TheoryData<string, string, string> data = [];

        foreach (var (authority, origin) in authorities)
        {
            foreach (var suffix in suffixes)
            {
                data.Add($"{authority} + {suffix}", $"rtsp://{authority}{suffix}", origin);
            }
        }

        return data;
    }

    public static TheoryData<string, string> AuthorityEdgeRowsThatRefuse()
    {
        TheoryData<string, string> data = [];

        data.Add("63-char label then non-ascii", $"rtsp://{new string('a', 63)}ü.example/x");
        data.Add("300-char host", $"rtsp://{new string('a', 300)}/x");
        data.Add("no slashes", "rtsp:cam/x");
        data.Add("one slash", "rtsp:/cam/x");
        data.Add("three slashes", "rtsp:///cam/x");
        data.Add("four slashes", "rtsp:////cam/x");
        data.Add("non-ascii before the marker", "rtsp:ü//cam/x");
        data.Add("unc non-ascii", @"\\kämera\x");
        data.Add("unc device path", @"\\?\UNC\cam\x");
        data.Add("fullwidth port", "rtsp://cam:５５４/x");
        data.Add("arabic-indic port", "rtsp://cam:\u0665\u0665\u0664/x");
        data.Add("fullwidth digit after port", "rtsp://cam:554４/x");
        data.Add("ipv6 non-ascii zone", "rtsp://[fe80::1%25ü]/x");
        data.Add("ipv6 unescaped non-ascii zone", "rtsp://[fe80::1%ü]/x");
        data.Add("ipv6 lrm zone", "rtsp://[fe80::1%25\u200E]/x");
        data.Add("dotted capital i", "rtsp://cam\u0130.example/x");
        data.Add("dotless i", "rtsp://cam\u0131.example/x");
        data.Add("long s", "rtsp://\u017Fcam/x");
        data.Add("kelvin sign", "rtsp://cam\u212A/x");
        data.Add("rlo around the url", "\u202Ertsp://cam/x\u202C");
        data.Add("lro around the url", "\u202Drtsp://cam/x\u202C");
        data.Add("isolate around the url", "\u2066rtsp://cam/x\u2069");
        data.Add("leading nbsp", "\u00A0rtsp://cam/x");
        data.Add("leading ideographic space", "\u3000rtsp://cam/x");
        data.Add("leading bom", "\uFEFFrtsp://cam/x");
        data.Add("leading line separator", "\u2028rtsp://cam/x");
        data.Add("leading nel", "\u0085rtsp://cam/x");

        return data;
    }

    public static TheoryData<string> ControlCharacterRows()
    {
        TheoryData<string> data = [];

        for (var character = 0; character < 0x20; character++)
        {
            data.Add($"rtsp://ca{(char)character}m/x");
        }

        data.Add("rtsp://ca\u007Fm/x");
        data.Add("rtsp://ca\u0085m/x");
        data.Add("rtsp://ca\u009Fm/x");

        return data;
    }

    [Theory]
    [MemberData(nameof(BackslashAuthorityRows))]
    public void A_backslash_authority_that_hides_from_the_written_scan_is_refused_by_the_host_check(
        string label,
        string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ShouldNotContain(Marker, Case.Insensitive, label);
        origin.ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Theory]
    [MemberData(nameof(NonAsciiOutsideTheAuthorityRows))]
    public void Non_ascii_text_outside_the_authority_still_renders_the_origin(
        string label,
        string input,
        string expected) =>
        UrlRedaction.ToOrigin(input).ShouldBe(expected, label);

    [Theory]
    [MemberData(nameof(AuthorityEdgeRowsThatRefuse))]
    public void An_authority_edge_renders_the_placeholder(string label, string input) =>
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, label);

    [Theory]
    [MemberData(nameof(ControlCharacterRows))]
    public void A_control_character_in_the_host_never_reaches_the_output(string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ShouldBeOneOf(UrlRedaction.Unparseable, "rtsp://cam");
    }

    [Theory]
    [InlineData("rtsp://cam//x", "rtsp://cam")]
    [InlineData("rtsp://cam/x//y", "rtsp://cam")]
    [InlineData(@"\\cam\x", "file://cam")]
    [InlineData(@"file:\\cam\x", "file://cam")]
    [InlineData(@"rtsp:\\cam/x", "rtsp://cam")]
    [InlineData("rtsp://cam:00554/x", "rtsp://cam:554")]
    [InlineData("rtsp://cam:65535/x", "rtsp://cam:65535")]
    [InlineData(" rtsp://cam/x", "rtsp://cam")]
    [InlineData("\r\nrtsp://cam/x", "rtsp://cam")]
    public void An_ascii_authority_in_an_unusual_spelling_renders_its_origin(string input, string expected) =>
        UrlRedaction.ToOrigin(input).ShouldBe(expected);

    // Built in the test body, not as theory rows: theory data is serialised through UTF-8, which turns a
    // lone surrogate into U+FFFD before the test ever sees it.
    [Fact]
    public void A_lone_surrogate_refuses_in_the_host_and_is_ignored_outside_the_authority()
    {
        List<string> offending = [];

        foreach (var lone in new[] { "\uD800", "\uDBFF", "\uDC00", "\uDFFF", "\uDC00\uD800" })
        {
            foreach (var input in new[] { $"rtsp://cam{lone}{Marker}/x", $"rtsp://{Marker}{lone}cam/x" })
            {
                var origin = UrlRedaction.ToOrigin(input);

                if (origin != UrlRedaction.Unparseable || UrlRedaction.ToOrigin(origin) != origin)
                {
                    offending.Add($"host U+{(int)lone[0]:X4}: {origin}");
                }
            }

            foreach (var input in new[] { $"rtsp://cam:8554/x{lone}", $"rtsp://cam:8554/x?{lone}", $"rtsp://cam:8554/x#{lone}" })
            {
                var origin = UrlRedaction.ToOrigin(input);

                if (origin != "rtsp://cam:8554")
                {
                    offending.Add($"outside U+{(int)lone[0]:X4}: {origin}");
                }
            }
        }

        offending.ShouldBeEmpty();
    }

    [Fact]
    public void A_63_character_label_and_a_253_character_host_render_unchanged()
    {
        var label = new string('a', 63);
        var host = string.Join('.', Enumerable.Repeat(label, 4));

        UrlRedaction.ToOrigin($"rtsp://{label}.example/x").ShouldBe($"rtsp://{label}.example");
        UrlRedaction.ToOrigin($"rtsp://{host}/x").ShouldBe($"rtsp://{host}");
    }

    [Fact]
    public void Lower_casing_does_not_follow_the_turkish_culture()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            UrlRedaction.ToOrigin("rtsp://CAMI.EXAMPLE/x").ShouldBe("rtsp://cami.example");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Two_urls_differing_only_in_non_ascii_path_text_reduce_to_one_origin() =>
        UrlRedaction.ToOrigin("rtsp://cam:8554/stream/ü").ShouldBe(UrlRedaction.ToOrigin("rtsp://cam:8554/stream/ß"));

    [Fact]
    public void A_url_that_becomes_the_placeholder_no_longer_matches_its_ascii_neighbour() =>
        UrlRedaction.ToOrigin("rtsp://kämera.example/x").ShouldNotBe(UrlRedaction.ToOrigin("rtsp://kamera.example/x"));

    public static TheoryData<string> EveryRowInThisFile()
    {
        TheoryData<string> data = [UrlRedaction.Unparseable, "rtsp://cam", "rtsp://cam:8554", "rtsp://[fd00::5]:8554"];

        foreach (var row in BackslashAuthorityRows())
        {
            data.Add((string)row[1]);
        }

        foreach (var row in NonAsciiOutsideTheAuthorityRows())
        {
            data.Add((string)row[1]);
        }

        foreach (var row in AuthorityEdgeRowsThatRefuse())
        {
            data.Add((string)row[1]);
        }

        foreach (var row in ControlCharacterRows())
        {
            data.Add(row);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRowInThisFile))]
    public void Reducing_an_origin_again_changes_nothing(string input)
    {
        var once = UrlRedaction.ToOrigin(input);

        UrlRedaction.ToOrigin(once).ShouldBe(once);
    }
}
