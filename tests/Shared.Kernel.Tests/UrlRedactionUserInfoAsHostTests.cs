using System.Globalization;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 381 / issue #2993. An unencoded #, ? or / inside a userinfo ends the authority early, so the
/// parser reads the secret as host or port and the real host as path, query or fragment. ToOrigin must
/// render the placeholder for any input with an @ after the authority, and keep rendering the origin for
/// a proper user:pass@host. Assertions are on the function's output, never on the input text; the rows
/// are generated in code.
/// </summary>
public class UrlRedactionUserInfoAsHostTests
{
    private static readonly string[] Schemes = ["rtsp", "rtsps", "http", "https"];

    private static readonly string[] Shapes = ["user:pass", "user:pass-tail", "user-only", "empty-user"];

    private static readonly string[] Secrets = ["12345", "4711", "1", "0x7f000001", "abc"];

    private static readonly string[] Delimiters = ["#", "?", "/", "\\"];

    private static readonly string[] HostParts = ["cam.local/s", "10.0.0.5:554/h264", "[fd00::5]:8554/x"];

    private sealed record Row(string Label, string Input, bool LeaksToday);

    private static string Userinfo(string shape, string secret, string delimiter) => shape switch
    {
        "user:pass" => $"admin:{secret}{delimiter}@",
        "user:pass-tail" => $"operator:{secret}{delimiter}!@",
        "user-only" => $"{secret}{delimiter}@",
        _ => $":{secret}{delimiter}@",
    };

    private static List<Row> Matrix()
    {
        List<Row> rows = [];

        foreach (var scheme in Schemes)
        {
            foreach (var shape in Shapes)
            {
                foreach (var secret in Secrets)
                {
                    foreach (var delimiter in Delimiters)
                    {
                        foreach (var host in HostParts)
                        {
                            var input = $"{scheme}://{Userinfo(shape, secret, delimiter)}{host}";

                            rows.Add(new Row(
                                $"{scheme} {shape} {secret} {delimiter} {host}",
                                input,
                                ParserReadsTheSecretAsTheHost(input, RealHost(host))));
                        }
                    }
                }
            }
        }

        return rows;
    }

    private static string RealHost(string hostPart)
    {
        var authority = hostPart[..hostPart.IndexOf('/')];

        return authority.StartsWith('[')
            ? authority[..(authority.IndexOf(']') + 1)]
            : authority.Split(':')[0];
    }

    // Derived from the runtime, not from a hand-written rule: the parser leaves the userinfo empty and
    // reports a host other than the one written after the @, so the text before the delimiter was read as host.
    private static bool ParserReadsTheSecretAsTheHost(string input, string realHost) =>
        Uri.TryCreate(input, UriKind.Absolute, out var uri)
        && uri.UserInfo.Length == 0
        && uri.Host != realHost;

    public static TheoryData<string, string> LeakingRows()
    {
        TheoryData<string, string> data = [];

        foreach (var row in Matrix().Where(row => row.LeaksToday))
        {
            data.Add(row.Label, row.Input);
        }

        return data;
    }

    public static TheoryData<string, string> NonLeakingRows()
    {
        TheoryData<string, string> data = [];

        foreach (var row in Matrix().Where(row => !row.LeaksToday))
        {
            data.Add(row.Label, row.Input);
        }

        return data;
    }

    private static readonly string[] PinnedLeaks =
    [
        "rtsp://admin:12345#@cam.local/s",
        "rtsp://admin:4711?@cam.local/s",
        "rtsp://admin:8080/x@cam.local/s",
        "rtsp://operator:2468#!@10.0.0.5:554/h264",
        "rtsp://12345#@cam/s",
        "rtsp://0x7f000001#@cam/s",
        "https://1#@cam/s",
        "http://12345#@cam.local/s",
        "rtsp://SECRET#@cam/s",
        "rtsp://a@admin:12345#@cam/s",
        "rtsp:\\\\admin:12345#@cam/s",
        "http:/\\admin:12345#@cam/s",
        " rtsp://admin:12345#@cam/s",
        "rtsp://[fd00::5]#@cam/s",
        "rtsps://admin:12345#@cam.local:8554/s",
        "rtsp://admin:12345#@[fd00::5]:8554/x",
        "rtsp://ADMIN:12345#@cam/s",
        "rtsp://admin:12345#@cam/s#frag@x",
    ];

    private static readonly string[] AcceptedOverRefusals =
    [
        "rtsp://cam/x@y",
        "rtsp://cam:554/x?mail=a@b",
        "rtsp://cam/x#a@b",
        "rtsp://cam:554/x?@",
        "rtsp://user:pass@cam/x@y",
        " rtsp://user:pass@cam/x",
    ];

    private static readonly (string Input, string Origin)[] Unchanged =
    [
        ("rtsp://user:pass@cam/x", "rtsp://cam"),
        ("rtsp://admin:SECRET@cam/x?token=SECRET", "rtsp://cam"),
        ("rtsp://admin:hunter2@10.0.5.12/h264", "rtsp://10.0.5.12"),
        ("rtsp://user:secret@camera-sim:8554/station-4", "rtsp://camera-sim:8554"),
        ("rtsp://admin:12345%23@cam/s", "rtsp://cam"),
        ("rtsp://admin:12345%2F@cam/s", "rtsp://cam"),
        ("rtsp://admin:12345%3F@cam/s", "rtsp://cam"),
        ("rtsp://admin:12345%5C@cam/s", "rtsp://cam"),
        ("rtsp://admin%40x:12345@cam/s", "rtsp://cam"),
        ("rtsp://admin:12345;@cam/s", "rtsp://cam"),
        ("rtsp://admin:12345 @cam/s", "rtsp://cam"),
        ("rtsp://cam/x%40y", "rtsp://cam"),
        ("rtsp://cam:554/stream", "rtsp://cam:554"),
        ("rtsp://10.0.5.5/h264", "rtsp://10.0.5.5"),
        ("rtsp://cam/", "rtsp://cam"),
        ("rtsps://cam/", "rtsps://cam"),
        ("http://cam/", "http://cam"),
        ("https://cam/", "https://cam"),
        ("rtsp://10.0.0.5:554/", "rtsp://10.0.0.5:554"),
        ("rtsps://10.0.0.5:554/", "rtsps://10.0.0.5:554"),
        ("http://10.0.0.5:554/", "http://10.0.0.5:554"),
        ("https://10.0.0.5:554/", "https://10.0.0.5:554"),
    ];

    public static TheoryData<string> PinnedLeakRows() => ToData(PinnedLeaks);

    public static TheoryData<string> OverRefusalRows() => ToData(AcceptedOverRefusals);

    public static TheoryData<string, string> UnchangedRows()
    {
        TheoryData<string, string> data = [];

        foreach (var (input, origin) in Unchanged)
        {
            data.Add(input, origin);
        }

        return data;
    }

    public static TheoryData<string> IdempotenceRows() =>
        ToData(
            Matrix().Where(row => row.LeaksToday).Select(row => row.Input)
                .Concat(PinnedLeaks)
                .Concat(AcceptedOverRefusals)
                .Concat(Unchanged.Select(pair => pair.Input))
                .ToArray());

    private static TheoryData<string> ToData(IEnumerable<string> values)
    {
        TheoryData<string> data = [];

        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LeakingRows))]
    public void A_userinfo_cut_short_by_a_delimiter_renders_the_placeholder(string label, string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Theory]
    [MemberData(nameof(NonLeakingRows))]
    public void A_shape_that_does_not_leak_renders_the_placeholder(string label, string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Fact]
    public void The_matrix_has_960_rows_of_which_396_leak_on_this_runtime()
    {
        var matrix = Matrix();

        matrix.Count.ShouldBe(960);
        matrix.Count(row => row.LeaksToday).ShouldBe(396);
    }

    [Fact]
    public void No_input_with_an_at_sign_after_the_authority_comes_out_as_anything_but_the_placeholder_or_the_real_origin()
    {
        string[] markers = ["//", "\\\\", "/\\", "\\/", "", "///"];
        string[] userinfos = ["admin:12345", "12345", "admin:1", "abc"];
        string[] suffixes = ["", "x"];
        string[] hosts = ["cam", "10.0.0.5:554"];
        List<string> offenders = [];
        var total = 0;

        foreach (var scheme in Schemes)
        {
            foreach (var marker in markers)
            {
                foreach (var userinfo in userinfos)
                {
                    foreach (var suffix in suffixes)
                    {
                        foreach (var host in hosts)
                        {
                            for (var code = 0; code <= 0x7F; code++)
                            {
                                var input = $"{scheme}:{marker}{userinfo}{(char)code}{suffix}@{host}/s";
                                var expected = $"{scheme}://{host}";
                                total++;

                                var output = UrlRedaction.ToOrigin(input);

                                if (output != UrlRedaction.Unparseable && output != expected)
                                {
                                    offenders.Add(
                                        string.Create(
                                            CultureInfo.InvariantCulture,
                                            $"U+{code:X4} {scheme} marker[{marker.Length}] {userinfo} {suffix} {host} -> {output}"));
                                }
                            }
                        }
                    }
                }
            }
        }

        total.ShouldBe(49152);
        offenders.Count.ShouldBe(
            0,
            $"{offenders.Count} of {total} inputs rendered neither the placeholder nor the real origin; first ten: "
            + string.Join(" | ", offenders.Take(10)));
    }

    [Theory]
    [MemberData(nameof(PinnedLeakRows))]
    public void A_pinned_leaking_address_renders_the_placeholder(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(OverRefusalRows))]
    public void An_at_sign_after_the_authority_renders_the_placeholder_even_without_a_credential(string input)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(UrlRedaction.Unparseable);
    }

    [Theory]
    [MemberData(nameof(UnchangedRows))]
    public void An_address_whose_userinfo_the_parser_consumed_still_renders_its_origin(string input, string origin)
    {
        UrlRedaction.ToOrigin(input).ShouldBe(origin);
    }

    [Theory]
    [MemberData(nameof(IdempotenceRows))]
    public void Redacting_an_already_redacted_address_changes_nothing(string input)
    {
        var once = UrlRedaction.ToOrigin(input);

        once.ShouldNotContain('@');
        UrlRedaction.ToOrigin(once).ShouldBe(once);
    }
}
