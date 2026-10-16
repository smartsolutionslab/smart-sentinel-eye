using System.Text;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 381 / issue #2993, adversarial pass over the userinfo rule in <see cref="UrlRedaction.ToOrigin"/>:
/// the authority is located the way the parser locates it, and an @ at or after its first terminator
/// renders the placeholder. These rows go after the scan itself — the scheme-detection edge, every
/// spelling of the authority marker, leading whitespace the parser trims, inputs with no scheme at all,
/// numeric hosts the parser normalises — plus the ordinary shapes that must keep rendering their origin.
/// Assertions are on the output; marker checks are case-insensitive because the host is lower-cased.
/// </summary>
public partial class UrlRedactionUserInfoAdversarialTests
{
    private const string Secret = "Zq9Secret";

    private static readonly string[] Schemes =
        ["rtsp", "RTSP", "rtsps", "http", "HTTPS", "ftp", "file", "FILE", "ws", "wss", "mailto", "unknownx", "a+b.c-d", "x1"];

    private static readonly string[] Markers = ["//", @"\\", @"/\", @"\/", "///", "////", "", "/", @"\", @"\\\", @"/\/"];

    private static readonly string[] Terminators = ["#", "?", "/", @"\", "##", "#?", @"/?#\"];

    private static readonly string[] AtSigns = ["@", "@@", ":@", "@:"];

    private static readonly string[] Hosts = ["cam.example", "10.0.0.5:554/h264", "[fd00::5]:8554/x"];

    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*://([a-z0-9._\-]+|\[[0-9a-f:.]+\])(:[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OriginShape();

    private static IEnumerable<string> Userinfos(string secret, string terminator, string at) =>
    [
        $"admin:{secret}{terminator}{at}",
        $"{secret}{terminator}{at}",
        $":{secret}{terminator}{at}",
        $"admin:{secret}{terminator}x{at}",
        $"{secret}:{secret}{terminator}{at}",
        $"admin{terminator}{secret}{at}",
        $"@admin:{secret}{terminator}{at}",
    ];

    [Fact]
    public void Every_scheme_and_marker_spelling_with_an_at_sign_after_a_terminator_renders_the_placeholder()
    {
        List<string> offenders = [];
        var total = 0;

        foreach (var scheme in Schemes)
        {
            foreach (var marker in Markers)
            {
                foreach (var terminator in Terminators)
                {
                    foreach (var at in AtSigns)
                    {
                        foreach (var host in Hosts)
                        {
                            foreach (var userinfo in Userinfos(Secret, terminator, at))
                            {
                                total++;
                                var input = $"{scheme}:{marker}{userinfo}{host}";

                                if (UrlRedaction.ToOrigin(input) != UrlRedaction.Unparseable)
                                {
                                    offenders.Add(input);
                                }
                            }
                        }
                    }
                }
            }
        }

        total.ShouldBe(90552);
        offenders.ShouldBeEmpty();
    }

    public static TheoryData<string> LeadingNoiseRows()
    {
        string[] prefixes = [" ", "  ", "\t", "\n", "\r\n", "\u0001", "\u001F", "\u007F", "\u00A0", "\uFEFF", "\u200E", "\u3000"];
        TheoryData<string> data = [];

        foreach (var prefix in prefixes)
        {
            foreach (var shape in new[] { "rtsp://admin:12345#@cam/x", "rtsp:\\\\admin:12345?@cam", "http:/admin:12345/@cam" })
            {
                data.Add(prefix + shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LeadingNoiseRows))]
    public void Whitespace_or_a_control_character_before_the_scheme_does_not_let_a_cut_short_userinfo_through(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData("admin:12345#@cam")]
    [InlineData("admin:12345?@cam")]
    [InlineData("//admin:12345#@cam/x")]
    [InlineData(@"\\admin:12345#@cam\share")]
    [InlineData("a:b://admin:12345#@cam")]
    [InlineData("rtsp:rtsp://admin:12345#@cam")]
    [InlineData("rtsp://rtsp://admin:12345#@cam")]
    [InlineData("r1+.-://admin:12345#@cam")]
    [InlineData("1rtsp://admin:12345#@cam")]
    [InlineData("rtsp://@admin:12345#@cam")]
    [InlineData("rtsp://admin:12345#@")]
    [InlineData("rtsp://#@")]
    [InlineData("rtsp:///@")]
    [InlineData("mailto:admin:12345#@cam")]
    [InlineData("http://[::1]:12345#@cam")]
    [InlineData("http://12345:12345#@cam")]
    [InlineData("http://0x7f000001#@cam")]
    [InlineData("http://017700000001#@cam")]
    [InlineData("http://127.1#@cam")]
    [InlineData("http://4294967295#@cam")]
    [InlineData("rtsp://4294967296#@cam")]
    [InlineData("http://99999999999999999999#@cam")]
    public void A_scheme_detection_edge_or_a_numeric_userinfo_renders_the_placeholder(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void A_long_run_of_marker_characters_or_delimiters_still_renders_the_placeholder(int length)
    {
        string[] inputs =
        [
            "rtsp:" + new string('/', length) + "admin:12345#@cam",
            "rtsp:" + new string('\\', length) + "admin:12345#@cam",
            "rtsp:" + string.Concat(Enumerable.Repeat(@"/\", length)) + "admin:12345#@cam",
            "rtsp://admin:12345" + new string('#', length) + "@cam",
            "rtsp://admin:12345#" + new string('x', length) + "@cam",
            new string('r', length) + "://admin:12345#@cam",
        ];

        foreach (var input in inputs)
        {
            UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
        }
    }

    [Theory]
    [InlineData("rtsp://cam:554/stream", "rtsp://cam:554")]
    [InlineData("rtsp://admin:12345@10.0.0.5:554/Streaming/Channels/101", "rtsp://10.0.0.5:554")]
    [InlineData("rtsp://root:pass@10.0.0.6/axis-media/media.amp?videocodec=h264", "rtsp://10.0.0.6")]
    [InlineData("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=0", "rtsp://10.0.0.5:554")]
    [InlineData("rtsp://user%40corp:p%23ss@10.0.0.7:554/live", "rtsp://10.0.0.7:554")]
    [InlineData("http://admin:12345%2F@cam/x", "http://cam")]
    [InlineData("rtsp://admin:pa$$w0rd!@10.0.0.9:554/h264Preview_01_main", "rtsp://10.0.0.9:554")]
    [InlineData("rtsp://admin:12345|@cam/x", "rtsp://cam")]
    [InlineData("rtsp://admin:12345 @cam/x", "rtsp://cam")]
    [InlineData("rtsp://admin:12345\t@cam/x", "rtsp://cam")]
    [InlineData("rtsp://user:pass@[fd00::5]:8554/x", "rtsp://[fd00::5]:8554")]
    [InlineData("rtsp://[fd00::5]:8554/x", "rtsp://[fd00::5]:8554")]
    [InlineData(@"rtsp:/\admin:12345@cam/x", "rtsp://cam")]
    [InlineData(@"rtsp:\/admin:12345@cam/x", "rtsp://cam")]
    [InlineData("RTSP://ADMIN:12345@CAM/x", "rtsp://cam")]
    [InlineData("rtsp://user:pass@cam?", "rtsp://cam")]
    [InlineData("rtsp://user:pass@cam#", "rtsp://cam")]
    [InlineData("rtsp://user:pass@cam/", "rtsp://cam")]
    [InlineData("rtsp://user:pass@cam:00554/x", "rtsp://cam:554")]
    [InlineData("rtsps://user:pass@cam:322/s", "rtsps://cam:322")]
    [InlineData("http://user:pass@cam:80/", "http://cam")]
    public void An_ordinary_camera_address_with_its_at_sign_before_the_terminator_keeps_its_origin(string input, string origin)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(origin);
    }

    [Fact]
    public void A_lone_surrogate_on_either_side_of_the_terminator_renders_the_placeholder_without_throwing()
    {
        UrlRedaction.ToOrigin("rtsp://admin:12345\uD800#@cam").ShouldBe(UrlRedaction.Unparseable);
        UrlRedaction.ToOrigin("rtsp://admin:12345#\uD800@cam").ShouldBe(UrlRedaction.Unparseable);
        UrlRedaction.ToOrigin("\uD800rtsp://admin:12345#@cam").ShouldBe(UrlRedaction.Unparseable);
        UrlRedaction.ToOrigin("file://admin:12345\uDC00#@cam").ShouldBe(UrlRedaction.Unparseable);
        UrlRedaction.ToOrigin("rtsp://user:pass@cam/\uD800").ShouldBe("rtsp://cam");
    }

    [Fact]
    public void A_seeded_fuzz_never_renders_a_token_written_before_the_last_at_sign_or_a_malformed_origin()
    {
        string[] pieces =
        [
            "rtsp", "http", "file", ":", ":", "/", "/", "\\", "\\", "//", "@", "@", "#", "?", ";", "[", "]", "%", "%40", "%23",
            Secret, "4711", "cam", "554", ".", "0x", "1", "fe80::", "%25", " ", "\u0000", "\t", "\uFF20", "\uFF0F", "\u200E", "~",
        ];
        var random = new Random(2993);
        var builder = new StringBuilder();
        List<string> offenders = [];

        for (var iteration = 0; iteration < 200_000; iteration++)
        {
            builder.Clear().Append(iteration % 2 == 0 ? "rtsp://" : "http://");
            var length = random.Next(1, 14);

            for (var piece = 0; piece < length; piece++)
            {
                builder.Append(pieces[random.Next(pieces.Length)]);
            }

            var input = builder.ToString();
            var output = UrlRedaction.ToOrigin(input);

            if (output != UrlRedaction.Unparseable
                && (!OriginShape().IsMatch(output)
                    || UrlRedaction.ToOrigin(output) != output
                    || LeaksATokenWrittenBeforeTheLastAtSign(input, output)))
            {
                offenders.Add(input);
            }
        }

        offenders.ShouldBeEmpty();
    }

    // The boundary of the rule, made explicit: U+FF20 is not an at sign to any parser, so this address
    // reads exactly like rtsp://admin:12345#cam, where nothing can tell a host from a credential.
    [Fact]
    public void A_fullwidth_at_sign_after_the_terminator_is_not_a_delimiter_and_is_not_refused()
    {
        UrlRedaction.ToOrigin("rtsp://admin:12345#＠cam/s").ShouldBe("rtsp://admin:12345");
        UrlRedaction.ToOrigin("rtsp://admin:12345#cam").ShouldBe("rtsp://admin:12345");
    }

    private static bool LeaksATokenWrittenBeforeTheLastAtSign(string input, string output)
    {
        var lastAt = input.LastIndexOf('@');

        return new[] { Secret, "4711" }.Any(token =>
        {
            var last = input.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);

            return last >= 0
                && last + token.Length <= lastAt
                && output.Contains(token, StringComparison.OrdinalIgnoreCase);
        });
    }
}
