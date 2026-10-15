using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Spec 377 (#2977) adversarial pass over <see cref="UrlRedaction.ToOrigin"/>, which the audit payload
/// redaction relies on for every camera URL. The credential marker sits in the userinfo, path, query or
/// fragment, spelled every way a vendor URL, a paste or a hostile caller might spell it; what comes out
/// must be the placeholder or a bare <c>scheme://host[:port]</c>, and must not carry any part of the
/// marker. The host itself is shown by design, so no case here hides the marker in the host.
/// </summary>
public partial class UrlRedactionAdversarialTests
{
    private const string MarkerTail = "CRET";

    public static TheoryData<string> CredentialSpellings() =>
    [
        "rtsp://user:SECRET@cam/x",
        "rtsp://:SECRET@cam/x",
        "rtsp://user:SECRET%40x@cam/x",
        "rtsp://SECRET@cam/x",
        "rtsp://SECRET:554@cam/x",
        "rtsp://user:SE/CRET@cam/x",
        "rtsp://user:SE?CRET@cam/x",
        "rtsp://user:SE#CRET@cam/x",
        "rtsp://cam/SECRET/x",
        "rtsp://cam/x?token=SECRET",
        "rtsp://cam/x#SECRET",
        "rtsp://cam?SECRET",
        "rtsp://cam#SECRET",
        "rtsp://cam?/SECRET",
        "rtsp://cam:554#x/SECRET",
        "rtsp://cam/%53E%43RET",
        "rtsp://cam/x?a=1&b=<SECRET>+",
        @"rtsp://cam\SECRET",
        @"rtsp://cam\SECRET/x",
        @"rtsp://cam\x\SECRET",
        "rtsp://cam/x@SECRET",
        "RTSP://CAM/SECRET",
        "rtsp://[fd00::5]:554/SECRET",
        "rtsp://[fe80::1%25SECRET]/x",
        "rtsp://[fe80::1%SECRET]/x",
        "rtsp://kämera.example/SECRET",
        "rtsp://cam./SECRET",
        "  rtsp://cam/SECRET  ",
        "\trtsp://cam/SECRET\r\n",
        "rtsp://cam/SE\r\nCRET",
        "rtsp://ca\nm/SECRET",
        "rtsp://cam/\0SECRET",
        "rtsp://ca\0m/SECRET",
        "rtsp://cam;pass=SECRET/x",
        "rtsp://cam:SECRET/x",
        "rtsp://cam:554;SECRET/x",
        "rtsp://cam%2FSECRET/x",
        "rtsp://cam /SECRET",
        "rtsp://:554/SECRET",
        "cam/SECRET",
        "//cam/SECRET",
        "file://srv/SECRET",
        @"\\srv\SECRET\x",
        @"C:\SECRET",
        "rtsp:SECRET",
        "rtsp:/SECRET",
        "rtsp:///SECRET",
    ];

    [Theory]
    [MemberData(nameof(CredentialSpellings))]
    public void No_part_of_a_credential_survives_the_reduction_to_an_origin(string url) =>
        AssertBareOriginWithoutMarker(UrlRedaction.ToOrigin(url));

    [Theory]
    [InlineData(2048)]
    [InlineData(65519)]
    [InlineData(70000)]
    public void No_part_of_a_credential_survives_in_a_very_long_url(int length)
    {
        const string prefix = "rtsp://cam/";
        const string suffix = "?token=SECRET";
        var url = prefix + new string('x', length - prefix.Length - suffix.Length) + suffix;

        AssertBareOriginWithoutMarker(UrlRedaction.ToOrigin(url));
    }

    [Fact]
    public void No_part_of_a_credential_survives_trailing_padding() =>
        AssertBareOriginWithoutMarker(UrlRedaction.ToOrigin("rtsp://cam/x?token=SECRET" + new string(' ', 5000)));

    private static void AssertBareOriginWithoutMarker(string rendered)
    {
        rendered.ShouldNotContain(MarkerTail, Case.Insensitive);
        (rendered == UrlRedaction.Unparseable || BareOrigin().IsMatch(rendered))
            .ShouldBeTrue($"'{rendered}' is neither the placeholder nor a bare scheme://host[:port]");
    }

    [Theory]
    [MemberData(nameof(CredentialSpellings))]
    public void The_reduction_is_idempotent_on_its_own_output(string url)
    {
        var once = UrlRedaction.ToOrigin(url);

        UrlRedaction.ToOrigin(once).ShouldBe(once);
    }

    [Fact]
    public void A_url_longer_than_the_old_uri_limit_is_reduced_to_its_origin_rather_than_the_placeholder()
    {
        var url = "rtsp://cam/" + new string('x', 70000) + "?token=SECRET";

        UrlRedaction.ToOrigin(url).ShouldBe("rtsp://cam");
    }

    [Fact]
    public void An_ipv6_zone_identifier_is_dropped_with_the_rest_of_the_detail()
    {
        UrlRedaction.ToOrigin("rtsp://[fe80::1%25eth0]/x").ShouldBe("rtsp://[fe80::1]");
    }

    [Fact]
    public void The_host_is_kept_by_design_so_a_secret_written_into_the_host_is_shown()
    {
        UrlRedaction.ToOrigin("rtsp://secret-host.example/x?token=SECRET").ShouldBe("rtsp://secret-host.example");
    }

    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*://[^/?#@\\\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex BareOrigin();
}
