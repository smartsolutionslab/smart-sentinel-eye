using System.Globalization;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 379 / issue #2989. <see cref="UrlRedaction.ToOrigin"/> shows a host only when it is plain ASCII;
/// anything else renders the placeholder. Every marker check is case-insensitive because ToOrigin
/// lower-cases the host, so an ordinal "SECRET" check would pass with the leak in place. Assertions are
/// on the function's output, never on the input text.
/// </summary>
public class UrlRedactionHostCharsetTests
{
    private const string Marker = "SECRET";

    private static readonly string[] Schemes = ["rtsp", "rtsps", "http"];

    // U+FF0F, U+FF1F, U+FF03, U+FF20, U+FF1A, U+FF06, U+FF1D, U+2044, U+2215, U+FE6B, U+00AD, U+3002,
    // U+200B, U+2060 are the fourteen named on #2989; the other twelve were added by the #2977/#2960 passes.
    private static readonly int[] CodePoints =
    [
        0xFF0F, 0xFF1F, 0xFF03, 0xFF20, 0xFF1A, 0xFF06, 0xFF1D, 0x2044, 0x2215, 0xFE6B, 0x00AD, 0x3002, 0x200B, 0x2060,
        0xFF3C, 0xFE56, 0xFE5F, 0xFF0E, 0xFF61, 0x200C, 0x200D, 0xFEFF, 0x2216, 0x29F8, 0xFF05, 0xFE68,
    ];

    private static readonly string[] IdnAndAstralInputs =
    [
        "rtsp://cam\U0001F600SECRET/x",
        "rtsp://SECRET\U0001F600cam/x",
        "rtsp://bücher.example/x",
        "rtsp://BÜCHER.example/x",
        "rtsp://cam／x/SECRET",
        "rtsp://ｃａｍ/x",
        "rtsp://cam。example/x",
        "rtsp://user：pass@cam/x",
        "rtsp://[fe80::1%25ｅth0]/x",
    ];

    // after-kv, after-kv-path and userinfo only reach the host when the equals or colon is the fullwidth one.
    private static readonly HashSet<string> ShapesThatNeedAFullwidthDelimiter =
        new(["after-kv", "after-kv-path", "userinfo"], StringComparer.Ordinal);

    private sealed record Row(string Label, string Shape, string Variant, string Input);

    private static List<Row> Matrix()
    {
        List<Row> rows = [];

        foreach (var scheme in Schemes)
        {
            foreach (var codePoint in CodePoints)
            {
                var l = char.ConvertFromUtf32(codePoint);

                foreach (var (variant, e, c) in new[] { ("ascii", "=", ":"), ("fullwidth", "＝", "：") })
                {
                    (string Shape, string HostPart)[] shapes =
                    [
                        ("after-direct", $"cam{l}{Marker}"),
                        ("after-kv", $"cam{l}token{e}{Marker}"),
                        ("after-kv-path", $"cam{l}token{e}{Marker}/x"),
                        ("before", $"{Marker}{l}cam"),
                        ("before-path", $"{Marker}{l}cam/x"),
                        ("before-port", $"{Marker}{l}cam:554/x"),
                        ("both", $"{Marker}{l}cam{l}{Marker}"),
                        ("userinfo", $"user{c}{Marker}{l}cam:554/x"),
                    ];

                    foreach (var (shape, hostPart) in shapes)
                    {
                        rows.Add(new Row(
                            $"{scheme} U+{codePoint:X4} {shape} {variant}",
                            shape,
                            variant,
                            $"{scheme}://{hostPart}"));
                    }
                }
            }
        }

        return rows;
    }

    private static bool LeaksToday(Row row) =>
        !ShapesThatNeedAFullwidthDelimiter.Contains(row.Shape) || row.Variant == "fullwidth";

    public static TheoryData<string, string> LeakingMatrixRows()
    {
        TheoryData<string, string> data = [];

        foreach (var row in Matrix().Where(LeaksToday))
        {
            data.Add(row.Label, row.Input);
        }

        return data;
    }

    public static TheoryData<string, string> NonLeakingMatrixRows()
    {
        TheoryData<string, string> data = [];

        foreach (var row in Matrix().Where(row => !LeaksToday(row)))
        {
            data.Add(row.Label, row.Input);
        }

        return data;
    }

    public static TheoryData<string, string> EveryMatrixAndIdnRow()
    {
        TheoryData<string, string> data = [];

        foreach (var row in Matrix())
        {
            data.Add(row.Label, row.Input);
        }

        foreach (var input in IdnAndAstralInputs)
        {
            data.Add($"idn {input}", input);
        }

        return data;
    }

    public static TheoryData<string> IdnAndAstralRows()
    {
        TheoryData<string> data = [];

        foreach (var input in IdnAndAstralInputs)
        {
            data.Add(input);
        }

        return data;
    }

    public static TheoryData<string, string> NormalisationBypassRows()
    {
        // 200E/200F/202A-202E: the parser strips them, leaving "camsecret". 212A: lower-cased to ASCII k.
        // 037E and 1FEF: normalised to ASCII ';' and a backtick.
        int[] codePoints = [0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x212A, 0x037E, 0x1FEF];
        TheoryData<string, string> data = [];

        foreach (var codePoint in codePoints)
        {
            data.Add($"U+{codePoint:X4}", $"rtsp://cam{char.ConvertFromUtf32(codePoint)}{Marker}/x");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LeakingMatrixRows))]
    public void A_host_carrying_a_non_ascii_character_renders_the_placeholder(string label, string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ShouldNotContain(Marker, Case.Insensitive, label);
        origin.ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Theory]
    [MemberData(nameof(NonLeakingMatrixRows))]
    public void A_non_ascii_character_before_an_ascii_delimiter_already_renders_the_placeholder(string label, string input) =>
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, label);

    [Fact]
    public void The_generated_matrix_has_1248_rows_of_which_1014_leak_today()
    {
        Matrix().Count.ShouldBe(1248);
        Matrix().Count(LeaksToday).ShouldBe(1014);
    }

    [Fact]
    public void No_code_point_above_ascii_survives_in_a_rendered_host()
    {
        List<string> offending = [];

        for (var codePoint = 0x80; codePoint <= 0xFFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var c = ((char)codePoint).ToString();

            foreach (var input in new[] { $"rtsp://cam{c}{Marker}/x", $"rtsp://{Marker}{c}cam/x" })
            {
                var origin = UrlRedaction.ToOrigin(input);

                if (origin != UrlRedaction.Unparseable
                    && (origin.Any(character => character > 0x7F)
                        || origin.Contains(Marker, StringComparison.OrdinalIgnoreCase)))
                {
                    offending.Add($"U+{codePoint:X4}");
                }
            }
        }

        offending.Count.ShouldBe(
            0,
            $"{offending.Count.ToString(CultureInfo.InvariantCulture)} offending outputs "
            + $"(first ten: {string.Join(", ", offending.Take(10))})");
    }

    [Theory]
    [MemberData(nameof(IdnAndAstralRows))]
    public void An_idn_or_astral_or_userinfo_or_zone_host_renders_the_placeholder(string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ShouldNotContain(Marker, Case.Insensitive);
        origin.ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(NormalisationBypassRows))]
    public void A_character_the_parser_normalises_away_still_renders_the_placeholder(string label, string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ToUpperInvariant().ShouldNotContain(Marker, Case.Sensitive, label);
        origin.ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Theory]
    [InlineData("rtsp://cam_1:8554/x", "rtsp://cam_1:8554")]
    [InlineData("rtsp://-cam-/x", "rtsp://-cam-")]
    [InlineData("rtsp://127.1/x", "rtsp://127.1")]
    [InlineData("rtsp://0x7f000001/x", "rtsp://0x7f000001")]
    [InlineData("http://0x7f000001/x", "http://127.0.0.1")]
    [InlineData("rtsp://[::ffff:10.0.0.1]/x", "rtsp://[::ffff:10.0.0.1]")]
    [InlineData("rtsp://[FD00::ABCD]/x", "rtsp://[fd00::abcd]")]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1]")]
    [InlineData("rtsp://CAM.Example/x", "rtsp://cam.example")]
    [InlineData("rtsp://cam./x", "rtsp://cam.")]
    [InlineData("rtsp://xn--bcher-kva.example/x", "rtsp://xn--bcher-kva.example")]
    [InlineData("rtsp://XN--BCHER-KVA.example/x", "rtsp://xn--bcher-kva.example")]
    [InlineData("rtsp://cam/x／SECRET", "rtsp://cam")]
    [InlineData("rtsp://cam/x?‎", "rtsp://cam")]
    [InlineData("rtsp:‎//cam/x", "<unparseable url>")]
    public void An_all_ascii_authority_renders_exactly_as_before(string input, string expected) =>
        UrlRedaction.ToOrigin(input).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(EveryMatrixAndIdnRow))]
    public void Reducing_an_origin_again_changes_nothing(string label, string input)
    {
        var once = UrlRedaction.ToOrigin(input);

        UrlRedaction.ToOrigin(once).ShouldBe(once, label);
    }
}
