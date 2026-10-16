using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 379 / issue #2989, third pass. The written-authority scan must locate the authority the way the
/// parser does (backslash markers, scheme-only forms), and a parser that throws on untrusted text must
/// not escape <see cref="UrlRedaction.ToOrigin"/>. Marker checks are case-insensitive because the host
/// is lower-cased.
/// </summary>
public partial class UrlRedactionAuthorityLocationTests
{
    private const string Marker = "SECRET";

    private static readonly string[] Schemes =
        ["rtsp", "rtsps", "http", "https", "ftp", "mailto", "zzz", "x-cam", "a1+b"];

    private static readonly string[] Markers = [@"\\", @"/\", @"\/", "//", ""];

    private static readonly int[] StrippedOrMapped =
        [0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x212A];

    private static readonly string[] Tails = ["/x//y", "?u=rtsp://y", "#//y", ""];

    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*://[A-Za-z0-9.\-_\[\]:]+$")]
    private static partial Regex BareOrigin();

    public static TheoryData<string> AuthorityLocationRows()
    {
        TheoryData<string> data = [];

        foreach (var input in AllAuthorityLocationInputs())
        {
            data.Add(input);
        }

        return data;
    }

    private static IEnumerable<string> AllAuthorityLocationInputs()
    {
        foreach (var scheme in Schemes)
        {
            foreach (var marker in Markers)
            {
                foreach (var codePoint in StrippedOrMapped)
                {
                    foreach (var tail in Tails)
                    {
                        yield return $"{scheme}:{marker}cam{(char)codePoint}{Marker}{tail}";
                    }
                }
            }
        }
    }

    public static TheoryData<string> ParserThrowsRows()
    {
        TheoryData<string> data = [];
        string[] markers = ["//", @"\\", @"/\", @"\/"];
        string[] followers = ["", "/x", "/SECRET@cam/x", "?q", "#f", @"\x"];

        foreach (var marker in markers)
        {
            foreach (var codePoint in StrippedOrMapped.Where(c => c != 0x212A))
            {
                foreach (var follower in followers)
                {
                    data.Add($"file:{marker}{(char)codePoint}{follower}");
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthorityLocationRows))]
    public void A_host_the_parser_rewrites_never_leaks_through_a_differently_spelled_authority_marker(string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ToUpperInvariant().ShouldNotContain(Marker);

        if (origin != UrlRedaction.Unparseable)
        {
            BareOrigin().IsMatch(origin).ShouldBeTrue($"'{origin}' from '{input}' is neither the placeholder nor a bare origin");
        }
    }

    [Theory]
    [MemberData(nameof(ParserThrowsRows))]
    public void A_parser_that_throws_on_the_text_yields_the_placeholder(string input)
    {
        string? origin = null;

        Should.NotThrow(() => origin = UrlRedaction.ToOrigin(input));

        origin.ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(AuthorityLocationRows))]
    public void Reducing_an_origin_again_changes_nothing(string input)
    {
        var once = UrlRedaction.ToOrigin(input);

        UrlRedaction.ToOrigin(once).ShouldBe(once);
    }

    [Theory]
    [InlineData(@"http:/\cam/x", "http://cam")]
    [InlineData(@"https:\/cam.example:8443/x?u=a//b", "https://cam.example:8443")]
    [InlineData(@"rtsps:/\[fd00::5]:8554/x", "rtsps://[fd00::5]:8554")]
    [InlineData(@"rtsp://cam:8554/x", "rtsp://cam:8554")]
    [InlineData(@"rtsp:\cam:554/x", UrlRedaction.Unparseable)]
    [InlineData(@"rtsp:\cam:8554/x", UrlRedaction.Unparseable)]
    [InlineData(@"http:\cam/x", UrlRedaction.Unparseable)]
    [InlineData(@"ftp:\10.0.5.12/x//y", UrlRedaction.Unparseable)]
    public void An_ascii_host_behind_a_backslash_marker_renders_as_it_does_today(string input, string expected) =>
        UrlRedaction.ToOrigin(input).ShouldBe(expected);
}
