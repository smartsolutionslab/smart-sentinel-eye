namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Characterisation (spec 421 / issue #3023, FR-002, ADR-0144), captured green on <c>cabe008a</c> before the camera
/// URL value objects learn to refuse a plain host holding a non-ASCII character. <see cref="UrlRedaction.ToOrigin"/>
/// is deliberately not taught the rule: it already withholds every such host as the placeholder, and renders the
/// accepted addresses as it always has. None of these rows may be edited by the change.
/// </summary>
public class UrlRedactionNonAsciiHostCharacterisationTests
{
    public static TheoryData<string, string> NonAsciiRows
    {
        get
        {
            TheoryData<string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.NonAscii)
            {
                rows.Add(label, address);
            }

            return rows;
        }
    }

    public static TheoryData<string, string, string> AcceptedRows
    {
        get
        {
            TheoryData<string, string, string> rows = [];
            foreach (var (label, address) in NonAsciiHostRows.Accepted)
            {
                rows.Add(label, address, ExpectedOrigins[address]);
            }

            return rows;
        }
    }

    private static Dictionary<string, string> ExpectedOrigins => new(StringComparer.Ordinal)
    {
        { "rtsp://10.0.0.66:554/live", "rtsp://10.0.0.66:554" },
        { "rtsp://xn--kamera-mnchen-wob.local/live", "rtsp://xn--kamera-mnchen-wob.local" },
        { "rtsp://xn--caf-dma/x", "rtsp://xn--caf-dma" },
        { "rtsp://cam./x", "rtsp://cam." },
        { "rtsp://cam_1/x", "rtsp://cam_1" },
        { "rtsp://a-b/x", "rtsp://a-b" },
        { "rtsp://0/x", "rtsp://0" },
        { "rtsp://CAM:554/S", "rtsp://cam:554" },
        { "rtsp://[fe80::1%25eth0]/s", "rtsp://[fe80::1]" },
        { "rtsp://cam/li\u3164ve", "rtsp://cam" },
        { "rtsp://cam/li\u202Eve", "rtsp://cam" },
        { "rtsp://cam/x?q=\u00FC", "rtsp://cam" },
        { "rtsp://cam/x#\u00FC", "rtsp://cam" },
    };

    [Theory]
    [MemberData(nameof(NonAsciiRows))]
    public void An_address_whose_plain_host_is_not_ascii_is_the_placeholder(string label, string url)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(UrlRedaction.Unparseable, label);
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public void An_address_that_stays_accepted_renders_the_origin_it_rendered_before(string label, string url, string expected)
    {
        UrlRedaction.ToOrigin(url).ShouldBe(expected, label);
    }

    [Fact]
    public void The_sets_hold_exactly_the_rows_the_spec_lists()
    {
        NonAsciiRows.Cast<object[]>().Count().ShouldBe(38);
        AcceptedRows.Cast<object[]>().Count().ShouldBe(13);
    }
}
