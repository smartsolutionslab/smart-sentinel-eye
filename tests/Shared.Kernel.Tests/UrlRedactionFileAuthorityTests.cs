using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 379 / issue #2989. A file: URL may carry extra separators after "://", and System.Uri reads the host
/// after them, so the span between "://" and the first separator is empty and trivially ASCII while the parser
/// strips or maps characters out of the real host. Every marker check is on the upper-cased output, because
/// ToOrigin lower-cases the host and U+212A lower-cases to an ASCII k. The host-character check is also pinned
/// here: a row only that check refuses.
/// </summary>
public partial class UrlRedactionFileAuthorityTests
{
    private const string Marker = "SECRET";

    private static readonly string[] Schemes = ["file", "FILE", "rtsp", "rtsps", "http", "https", "foo"];

    private static readonly int[] CodePoints = [0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x212A];

    private static readonly string[] Tails = ["/x", string.Empty];

    [GeneratedRegex(@"^[a-z0-9+.-]+://[A-Za-z0-9.\-_\[\]:]+$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex RenderedOrigin();

    private static List<string> ExtraSeparators()
    {
        List<string> extras = [string.Empty];

        for (var length = 1; length <= 3; length++)
        {
            for (var bits = 0; bits < 1 << length; bits++)
            {
                var extra = string.Concat(
                    Enumerable.Range(0, length).Select(index => ((bits >> index) & 1) == 0 ? '/' : '\\'));
                extras.Add(extra);
            }
        }

        return extras;
    }

    public static TheoryData<string, string> MatrixRows()
    {
        TheoryData<string, string> data = [];

        foreach (var scheme in Schemes)
        {
            foreach (var extra in ExtraSeparators())
            {
                foreach (var codePoint in CodePoints)
                {
                    var invisible = char.ConvertFromUtf32(codePoint);

                    foreach (var (placement, host) in new[]
                    {
                        ("before", $"{Marker}{invisible}cam"),
                        ("after", $"cam{invisible}{Marker}"),
                    })
                    {
                        foreach (var tail in Tails)
                        {
                            var label = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{scheme} extra[{extra.Replace('\\', '|')}] U+{codePoint:X4} {placement} tail[{tail}]");
                            data.Add(label, $"{scheme}://{extra}{host}{tail}");
                        }
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void A_host_the_parser_strips_or_maps_never_reaches_the_rendered_origin(string label, string input)
    {
        var origin = UrlRedaction.ToOrigin(input);

        origin.ToUpperInvariant().ShouldNotContain(Marker, Case.Sensitive, label);
        (origin == UrlRedaction.Unparseable || RenderedOrigin().IsMatch(origin)).ShouldBeTrue(
            $"{label}: rendered '{origin}'");
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void Reducing_an_origin_again_changes_nothing(string label, string input)
    {
        var once = UrlRedaction.ToOrigin(input);

        UrlRedaction.ToOrigin(once).ShouldBe(once, label);
    }

    [Theory]
    [InlineData("file:////cam/x", "file://cam")]
    [InlineData("file://host/x", "file://host")]
    [InlineData("rtsp://cam/x", "rtsp://cam")]
    [InlineData("FILE:////cam/x", "file://cam")]
    [InlineData("file:///\\cam/x", "file://cam")]
    public void An_ascii_only_host_renders_as_before(string input, string expected) =>
        UrlRedaction.ToOrigin(input).ShouldBe(expected);

    [Fact]
    public void A_file_host_holding_a_non_ascii_letter_is_refused_by_the_host_check_alone()
    {
        var input = "file:////cam" + (char)0x00E4 + Marker + "/x";

        var origin = UrlRedaction.ToOrigin(input);

        origin.ToUpperInvariant().ShouldNotContain(Marker, Case.Sensitive);
        origin.ShouldBe(UrlRedaction.Unparseable);
    }
}
